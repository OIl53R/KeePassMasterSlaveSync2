using KeePass;
using KeePassLib;
using KeePassLib.Collections;
using KeePassLib.Interfaces;
using KeePassLib.Keys;
using KeePassLib.Security;
using KeePassLib.Serialization;
using KeePassLib.Utility;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace KeePassMasterSlaveSync
{
    public class Sync
    {
        private static IOConnectionInfo connectionInfo = null;
        private static CompositeKey MasterKey = null;
        private static bool inSlave = false;
        private static List<string> EditedDatabases = new List<string>();

        private static string currentJob = "";

        public class SlaveBean
        {
            public string TargetFilePath;
            public ProtectedString Password;
            public string KeyFilePath;
            public bool Disabled;
            public SlaveBean(string targetFilePath,
                ProtectedString password,
                string keyFilePath,
                bool disabled)
            {
                TargetFilePath = targetFilePath;
                Password = password;
                KeyFilePath = keyFilePath;
                Disabled = disabled;
            }
            public override int GetHashCode()
            {
                return this.TargetFilePath.GetHashCode();
            }
            public override bool Equals(object obj)
            {
                return obj != null
                    && obj is SlaveBean
                    && ((SlaveBean)obj).TargetFilePath == this.TargetFilePath;
            }
        }

        public static void StartSync(PwDatabase sourceDb)
        {
            // Update EditedDatabases
            EditedDatabases.Add(sourceDb.IOConnectionInfo.Path);

            // Get the master database path and data
            connectionInfo = sourceDb.IOConnectionInfo;
            MasterKey = sourceDb.MasterKey;

            // Get all entries out of the group "MSSyncJobs"
            PwGroup settingsGroup = sourceDb.RootGroup.Groups.FirstOrDefault(g => g.Name == "MSSyncJobs");
            if (settingsGroup == null)
            {
                return;
            }
            IEnumerable<PwEntry> jobSettings = settingsGroup.Entries;

            //This will be the list of the slave jobs to perform
            List<SlaveBean> slaves = new List<SlaveBean>();

            // Loop through all found entries - each one is a Sync job 
            foreach (var settingsEntry in jobSettings)
            {
                // Load settings for this job
                var settings = Settings.Parse(settingsEntry, sourceDb);
                currentJob = settingsEntry.Strings.GetSafe(PwDefs.TitleField).ReadString();

                // If this is true don't perform the job since it has to be done from the master DB
                if (settings.IsSlave
                    || CheckKeyFile(sourceDb, settings, settingsEntry)
                    || CheckTagOrGroup(settings, settingsEntry)
                    || CheckTargetFilePath(settings, settingsEntry, sourceDb)
                    || CheckPasswordOrKeyfile(settings, settingsEntry)
                    || settings.Disabled)
                {
                    continue;
                }

                SlaveBean slave = new SlaveBean(
                        settings.TargetFilePath,
                        settings.Password,
                        settings.KeyFilePath,
                        settings.Disabled
                    );
                //Prevent repeated slave databases
                if (!slaves.Contains(slave))
                {
                    slaves.Add(slave);
                }

                // Update Edited Databases
                if (!EditedDatabases.Contains(settings.TargetFilePath))
                    EditedDatabases.Add(settings.TargetFilePath);

                try
                {
                    // Execute the export 
                    SyncToDb(sourceDb, settings);
                }
                catch (Exception e)
                {
                    MessageService.ShowWarning("Synchronization failed:", e);
                }
            }

            //Start Synchronization from slaves
            foreach (SlaveBean s in slaves)
            {
                // Create a key for the target database
                CompositeKey key = CreateCompositeKey(s.Password, s.KeyFilePath);

                // Create or open the target database
                PwDatabase pwDatabase = OpenTargetDatabase(s.TargetFilePath, key);

                StartSyncAgain(pwDatabase);
            }
            UpdateOpenedDB(sourceDb.IOConnectionInfo.Path);
            connectionInfo = null;
            MasterKey = null;
            inSlave = false;
            currentJob = "";
        }

        public static void StartSyncAgain(PwDatabase sourceDb)
        {
            // Get all entries out of the group "MSSyncJobs". Each one is a sync job
            PwGroup settingsGroup = sourceDb.RootGroup.Groups.FirstOrDefault(g => g.Name == "MSSyncJobs");
            if (settingsGroup == null)
            {
                return;
            }
            IEnumerable<PwEntry> jobSettings = settingsGroup.Entries;

            // Loop through all found entries - each one is a sync job 
            foreach (var settingsEntry in jobSettings)
            {
                // Load settings for this job
                var settings = Settings.Parse(settingsEntry, sourceDb);
                currentJob = settingsEntry.Strings.GetSafe(PwDefs.TitleField).ReadString();

                // Skip disabled/expired jobs
                if (settings.Disabled)
                    continue;

                if (CheckTargetFilePath(settings, settingsEntry, sourceDb))
                    continue;

                if (settings.TargetFilePath == connectionInfo.Path)
                {
                    inSlave = true;
                }
                else
                {
                    if (CheckKeyFile(sourceDb, settings, settingsEntry))
                        continue;

                    if (CheckPasswordOrKeyfile(settings, settingsEntry))
                        continue;
                }

                if (CheckTagOrGroup(settings, settingsEntry))
                    continue;

                // Update Edited Databases
                if (!EditedDatabases.Contains(settings.TargetFilePath))
                    EditedDatabases.Add(settings.TargetFilePath);

                try
                {
                    // Execute the export 
                    SyncToDb(sourceDb, settings);
                }
                catch (Exception e)
                {
                    MessageService.ShowWarning("Synchronization failed:", e);
                }
                inSlave = false;
            }
        }

        public static void UpdateOpenedDB(string pathToMaster)
        {
            if (EditedDatabases.Count() > 1) // Not just the Master DB
            {
                List<KeePass.UI.PwDocument> openedDocuments = Program.MainForm.DocumentManager.Documents;
                List<KeePass.UI.PwDocument> openedEditedDocuments = openedDocuments.Where(d =>
                    EditedDatabases.Contains(d.Database.IOConnectionInfo.Path)).ToList();
                List<KeePass.UI.PwDocument> unlockedDocs = new List<KeePass.UI.PwDocument>(openedEditedDocuments.Where(oED => !Program.MainForm.IsFileLocked(oED)));
                foreach (KeePass.UI.PwDocument doc in unlockedDocs)
                {
                    openedEditedDocuments.Remove(doc);
                    var db = doc.Database;
                    var key = db.MasterKey;
                    var ioc = db.IOConnectionInfo;
                    Program.MainForm.DocumentManager.CloseDatabase(doc.Database);
                    Program.MainForm.OpenDatabase(ioc, key, true);
                }
                KeePass.UI.PwDocument masterDoc = Program.MainForm.DocumentManager.Documents.Where(d =>
                    d.Database.IOConnectionInfo.Path == pathToMaster).FirstOrDefault();
                Program.MainForm.MakeDocumentActive(masterDoc);
            }
            EditedDatabases.Clear();
        }

        private static Boolean CheckKeyFile(PwDatabase sourceDb, Settings settings, PwEntry settingsEntry)
        {
            // If a key file is given it must exist.
            if (!string.IsNullOrEmpty(settings.KeyFilePath))
            {
                // Default to same folder as sourceDb for the keyfile if no directory is specified
                if (!Path.IsPathRooted(settings.KeyFilePath))
                {
                    string sourceDbPath = Path.GetDirectoryName(sourceDb.IOConnectionInfo.Path);
                    if (sourceDbPath != null)
                    {
                        settings.KeyFilePath = Path.Combine(sourceDbPath, settings.KeyFilePath);
                    }
                }

                if (!File.Exists(settings.KeyFilePath))
                {
                    MessageService.ShowWarning("MasterSlaveSync: Keyfile is given but could not be found for: " +
                                               settingsEntry.Strings.ReadSafe("Title"), settings.KeyFilePath);
                    return true;
                }
            }

            return false;
        }

        private static bool CheckTagOrGroup(Settings settings, PwEntry settingsEntry)
        {
            // Require at least one of Tag or Group
            if (string.IsNullOrEmpty(settings.Tag) && string.IsNullOrEmpty(settings.Group))
            {
                MessageService.ShowWarning("MasterSlaveSync: Missing Tag or Group for: " +
                                           settingsEntry.Strings.ReadSafe("Title"));
                return true;
            }

            return false;
        }

        private static bool CheckTargetFilePath(Settings settings, PwEntry settingsEntry, PwDatabase sourceDb)
        {
            // Require targetFilePath
            if (string.IsNullOrEmpty(settings.TargetFilePath))
            {
                MessageService.ShowWarning("MasterSlaveSync: Missing TargetFilePath for: " +
                                           settingsEntry.Strings.ReadSafe("Title"));
                return true;
            }

            // Default to same folder as sourceDb for the keyfile if no directory is specified
            if (!Path.IsPathRooted(settings.TargetFilePath))
            {
                string sourceDbPath = Path.GetDirectoryName(sourceDb.IOConnectionInfo.Path);
                if (sourceDbPath != null)
                {
                    settings.TargetFilePath = Path.Combine(sourceDbPath, settings.TargetFilePath);
                }
            }

            if (!File.Exists(settings.TargetFilePath))
            {
                MessageService.ShowWarning("MasterSlaveSync: Slave Database not found for: " +
                                           settingsEntry.Strings.ReadSafe("Title"));
                return true;
            }

            return false;
        }

        private static bool CheckPasswordOrKeyfile(Settings settings, PwEntry settingsEntry)
        {
            // Require at least one of Password or KeyFilePath.
            if (settings.Password.IsEmpty && !File.Exists(settings.KeyFilePath))
            {
                MessageService.ShowWarning("MasterSlaveSync: Missing Password or valid KeyFilePath for: " +
                                           settingsEntry.Strings.ReadSafe("Title"));
                return true;
            }

            return false;
        }

        /// <summary>
        /// Exports all entries with the given tag to a new database at the given path.
        /// </summary>
        /// <param name="sourceDb">The source database.</param>
        /// <param name="settings">The settings for this job.</param>
        private static void SyncToDb(PwDatabase sourceDb, Settings settings)
        {
            // Create a key for the target database
            CompositeKey key = null;
            if (inSlave)
                key = MasterKey;
            else
                key = CreateCompositeKey(settings.Password, settings.KeyFilePath);

            // Create or open the target database
            PwDatabase targetDb = OpenTargetDatabase(settings.TargetFilePath, key);

            // Assign the properties of the source root group to the target root group
            HandleCustomIcon(targetDb, sourceDb, sourceDb.RootGroup);

            // Find all entries matching the tag and/or group
            PwObjectList<PwEntry> entries = GetMatching(sourceDb, settings);

            // Copy all entries to the new database
            CopyEntriesAndGroups(sourceDb, settings, entries, targetDb);

            //Delete slave entries that match Master settings (But not in master)
            SelectEntriesAndDelete(entries, sourceDb, targetDb);

            // Save all changes to the DB
            sourceDb.Save(new NullStatusLogger());
            targetDb.Save(new NullStatusLogger());
        }

        private static CompositeKey CreateCompositeKey(ProtectedString password, string keyFilePath)
        {
            CompositeKey key = new CompositeKey();

            if (!password.IsEmpty)
            {
                IUserKey mKeyPass = new KcpPassword(password.ReadUtf8());
                key.AddUserKey(mKeyPass);
            }

            // Load a keyfile for the target database if requested (and add it to the key)
            if (!string.IsNullOrEmpty(keyFilePath))
            {
                IUserKey mKeyFile = new KcpKeyFile(keyFilePath);
                key.AddUserKey(mKeyFile);
            }

            return key;
        }

        private static PwDatabase OpenTargetDatabase(string targetFilePath, CompositeKey key)
        {
            // Create a new database 
            PwDatabase targetDatabase = new PwDatabase();

            // Connect the database object to the existing database
            targetDatabase.Open(new IOConnectionInfo()
            {
                Path = targetFilePath
            }, key, null);

            return targetDatabase;
        }

        /// <summary>
        /// Copies the custom icons required for this group to the target database.
        /// </summary>
        /// <param name="targetDatabase">The target database where to add the icons.</param>
        /// <param name="sourceDatabase">The source database where to get the icons from.</param>
        /// <param name="sourceGroup">The source group which icon should be copied (if it is custom).</param>
        private static void HandleCustomIcon(PwDatabase targetDatabase, PwDatabase sourceDatabase, PwGroup sourceGroup)
        {
            // Does the group not use a custom icon or is it already in the target database
            if (sourceGroup.CustomIconUuid.Equals(PwUuid.Zero) ||
                targetDatabase.GetCustomIconIndex(sourceGroup.CustomIconUuid) != -1)
            {
                return;
            }

            // Check if the custom icon really is in the source database
            int iconIndex = sourceDatabase.GetCustomIconIndex(sourceGroup.CustomIconUuid);
            if (iconIndex < 0 || iconIndex > sourceDatabase.CustomIcons.Count - 1)
            {
                MessageService.ShowWarning("Can't locate custom icon (" + sourceGroup.CustomIconUuid.ToHexString() +
                                           ") for group " + sourceGroup.Name);
            }

            // Get the custom icon from the source database
            PwCustomIcon customIcon = sourceDatabase.CustomIcons[iconIndex];

            // Copy the custom icon to the target database
            targetDatabase.CustomIcons.Add(customIcon);
        }

        /// <summary>
        /// Copies the custom icons required for this group to the target database.
        /// </summary>
        /// <param name="targetDatabase">The target database where to add the icons.</param>
        /// <param name="sourceDb">The source database where to get the icons from.</param>
        /// <param name="entry">The entry which icon should be copied (if it is custom).</param>
        private static void HandleCustomIcon(PwDatabase targetDatabase, PwDatabase sourceDb, PwEntry entry)
        {
            // Does the entry not use a custom icon or is it already in the target database
            if (entry.CustomIconUuid.Equals(PwUuid.Zero) ||
                targetDatabase.GetCustomIconIndex(entry.CustomIconUuid) != -1)
            {
                return;
            }

            // Check if the custom icon really is in the source database
            int iconIndex = sourceDb.GetCustomIconIndex(entry.CustomIconUuid);
            if (iconIndex < 0 || iconIndex > sourceDb.CustomIcons.Count - 1)
            {
                MessageService.ShowWarning("Can't locate custom icon (" + entry.CustomIconUuid.ToHexString() +
                                           ") for entry " + entry.Strings.ReadSafe("Title"));
            }

            // Get the custom icon from the source database
            PwCustomIcon customIcon = sourceDb.CustomIcons[iconIndex];

            // Copy the custom icon to the target database
            targetDatabase.CustomIcons.Add(customIcon);
        }

        private static List<PwGroup> GetGroup(PwDatabase sourceDb, string group)
        {
            List<PwGroup> groupToExport = new List<PwGroup>();
            if (group.Contains('/'))
            {
                PwGroup currGroup = null;
                string path = "";
                PwObjectList<PwGroup> groups = sourceDb.RootGroup.GetGroups(false);
                foreach (string subgroup in group.Split('/'))
                {
                    path += "/" + subgroup;
                    foreach (var g in groups)
                    {
                        if (g.Name == subgroup)
                        {
                            currGroup = g;
                        }
                    }
                    if (currGroup == null)
                    {
                        MessageService.ShowWarning("Path " + path + " to group not found");
                    }
                    else
                    {
                        groupToExport.Add(currGroup);
                    }
                }
            }
            else
            {
                groupToExport.AddRange(sourceDb.RootGroup.GetFlatGroupList().Where(g => g.Name == group));
            }
            if (groupToExport.Count == 0)
            {
                MessageService.ShowWarning("No group with the name " + group + " found.");
            }
            return groupToExport;
        }

        private static PwObjectList<PwEntry> AddEntries(PwObjectList<PwEntry> entries, PwObjectList<PwEntry> newEntries)
        {
            // Prevent duplicated entries
            IEnumerable<PwUuid> existingUuids = entries.Select(x => x.Uuid);
            List<PwEntry> entriesToAdd = newEntries.Where(x => !existingUuids.Contains(x.Uuid)).ToList();
            entries.Add(entriesToAdd);
            //IEnumerable<string> entriesNames = entriesToAdd.Select(x => x.Strings.Get("Title").ReadString());
            //MessageService.ShowInfo("New Entries:\r\n" + string.Join(",\r\n", entriesNames));
            return entries;
        }

        private static PwObjectList<PwEntry> GetMatching(PwDatabase sourceDb, Settings settings)
        {
            PwObjectList<PwEntry> entries = new PwObjectList<PwEntry>();

            if (!string.IsNullOrEmpty(settings.Tag) && string.IsNullOrEmpty(settings.Group))
            {
                // Tag only export
                // Support multiple tag (Tag1,Tag2)
                string cleanTag = settings.Tag.Replace(System.Environment.NewLine, "");
                foreach (string tag in cleanTag.Split(','))
                {
                    PwObjectList<PwEntry> tagEntries = new PwObjectList<PwEntry>();
                    sourceDb.RootGroup.FindEntriesByTag(tag, tagEntries, true);
                    entries = AddEntries(entries, tagEntries);
                }
            }
            else if (string.IsNullOrEmpty(settings.Tag) && !string.IsNullOrEmpty(settings.Group))
            {
                // Support multiple group (Group1,Group2)
                string cleanGroup = settings.Group.Replace(System.Environment.NewLine, "");
                foreach (string group in cleanGroup.Split(','))
                {
                    List<PwGroup> groupToExport = GetGroup(sourceDb, group);
                    foreach (PwGroup g in groupToExport)
                    {
                        PwObjectList<PwEntry> groupEntries = g.GetEntries(true);
                        entries = AddEntries(entries, groupEntries);
                    }
                }
            }
            else if (!string.IsNullOrEmpty(settings.Tag) && !string.IsNullOrEmpty(settings.Group))
            {
                // Tag and group export
                string cleanGroup = settings.Group.Replace(System.Environment.NewLine, "");
                foreach (string group in cleanGroup.Split(','))
                {
                    List<PwGroup> groupToExport = GetGroup(sourceDb, group);
                    foreach (PwGroup g in groupToExport)
                    {
                        string cleanTag = settings.Tag.Replace(System.Environment.NewLine, "");
                        foreach (string tag in cleanTag.Split(','))
                        {
                            PwObjectList<PwEntry> tagEntries = new PwObjectList<PwEntry>();
                            g.FindEntriesByTag(tag, tagEntries, true);
                            entries = AddEntries(entries, tagEntries);
                        }
                    }
                }
            }
            else
            {
                throw new ArgumentException("At least one of Tag or Group Name must be set.");
            }
            return entries;
        }

        private static bool isInTrash(PwGroup group)
        {
            PwGroup tmp = group;
            while (tmp.ParentGroup != null)
            {
                //MessageService.ShowInfo("Checking group Icon " + tmp.Name + " iconId " + tmp.IconId);
                if (tmp.IconId == PwIcon.TrashBin)
                {
                    return true;
                }
                tmp = tmp.ParentGroup;
            }
            return false;
        }

        private static int getParentsUuids(PwGroup group)
        {
            int res = group.Uuid.GetHashCode();
            PwGroup tmp = group;
            while (group.ParentGroup != null)
            {
                group = group.ParentGroup;
                res += group.Uuid.GetHashCode();
            }
            return res;
        }

        private static void CopyEntriesAndGroups(PwDatabase sourceDb, Settings settings, PwObjectList<PwEntry> entries,
            PwDatabase targetDatabase)
        {
            foreach (PwEntry entry in entries)
            {
                try
                {
                    // Get (or create in case its the first sync) the target group in the target database (including hierarchy)
                    PwGroup targetGroup = TargetGroupInDatebase(entry, targetDatabase, sourceDb);
                    PwEntry peNew = targetGroup.GetEntries(true).Where(x => x.Uuid.GetHashCode() == entry.Uuid.GetHashCode()).FirstOrDefault();
                    // Check if the target entry is newer than the source entry  && peNew.LastModificationTime > entry.LastModificationTime
                    if (peNew != null && peNew.LastModificationTime.CompareTo(entry.LastModificationTime) > 0
                        && getParentsUuids(peNew.ParentGroup) == getParentsUuids(targetGroup))
                    {
                        //MessageService.ShowInfo("Modifying Entry " + entry.Strings.Get("Title").ReadString());
                        CloneEntry(targetDatabase, sourceDb, peNew, entry, targetGroup, settings);
                        continue;
                    }

                    // Handle Duplicates entries' Uuids
                    PwEntry duplicatedEntry = targetDatabase.RootGroup.GetEntries(true).Where(x => x.Uuid.GetHashCode() == entry.Uuid.GetHashCode()).FirstOrDefault();
                    if (duplicatedEntry != null && getParentsUuids(duplicatedEntry.ParentGroup) != getParentsUuids(targetGroup))
                    {
                        DeleteEntry(duplicatedEntry, targetDatabase);
                    }
                    if (!isInTrash(entry.ParentGroup))
                    {
                        CloneEntry(sourceDb, targetDatabase, entry, peNew, targetGroup, settings);
                    }
                    deleteClean(targetDatabase);
                }
                catch (Exception e)
                {
                    MessageService.ShowInfo("Error " + e.Message);
                }
            }
        }

        /// <summary>
        /// Get or create the target group of an entry in the target database (including hierarchy).
        /// </summary>
        /// <param name="entry">An entry wich is located in the folder with the target structure.</param>
        /// <param name="targetDatabase">The target database in which the folder structure should be created.</param>
        /// <param name="sourceDatabase">The source database from which the folder properties should be taken.</param>
        /// <returns>The target folder in the target database.</returns>
        private static PwGroup TargetGroupInDatebase(PwEntry entry, PwDatabase targetDatabase, PwDatabase sourceDatabase)
        {
            // Collect all group names from the entry up to the root group
            PwGroup group = entry.ParentGroup;
            List<PwUuid> list = new List<PwUuid>();

            while (group != null)
            {
                list.Add(group.Uuid);
                group = group.ParentGroup;
            }

            // Remove root group (we already changed the root group name)
            list.RemoveAt(list.Count - 1);
            // groups are in a bottom-up oder -> reverse to get top-down
            list.Reverse();

            // Create group structure for the new entry (copying group properties)
            PwGroup lastGroup = targetDatabase.RootGroup;
            foreach (PwUuid id in list)
            {
                // Does the target group already exist?
                PwGroup newGroup = lastGroup.FindGroup(id, false);
                if (newGroup != null)
                {
                    lastGroup = newGroup;
                    continue;
                }

                // Get the source group
                PwGroup sourceGroup = sourceDatabase.RootGroup.FindGroup(id, true);

                // Create a new group and asign all properties from the source group
                newGroup = new PwGroup();
                newGroup.AssignProperties(sourceGroup, false, true);
                HandleCustomIcon(targetDatabase, sourceDatabase, sourceGroup);

                // Add the new group at the right position in the target database
                lastGroup.AddGroup(newGroup, true);

                lastGroup = newGroup;
            }

            // Return the target folder (leaf folder)
            return lastGroup;
        }

        private static void CloneEntry(PwDatabase sourceDb, PwDatabase targetDb, PwEntry sourceEntry,
            PwEntry targetEntry, PwGroup targetGroup, Settings settings)
        {
            // Was no existing entry in the target database found?
            if (targetEntry == null)
            {
                // Create a new entry
                targetEntry = new PwEntry(false, false)
                {
                    Uuid = sourceEntry.Uuid
                };

                //targetEntry = sourceEntry.CloneDeep();

                // Add entry to the target group in the new database
                targetGroup.AddEntry(targetEntry, true);
            }

            // Clone entry properties if ExportUserAndPassOnly is false
            if (!settings.ExportUserAndPassOnly)
            {
                targetEntry.AssignProperties(sourceEntry, false, true, true);
            }

            // This is neccesary to support field refferences. Maybe notes field too?
            string[] fieldNames = { PwDefs.TitleField , PwDefs.UserNameField ,
                PwDefs.PasswordField, PwDefs.UrlField };

            foreach (string fieldName in fieldNames)
                targetEntry.Strings.Set(fieldName, Settings.GetFieldWRef(sourceEntry, sourceDb, fieldName));

            // Handle custom icon
            HandleCustomIcon(targetDb, sourceDb, sourceEntry);
        }

        private static void SelectEntriesAndDelete(PwObjectList<PwEntry> entries, PwDatabase sourceDb, PwDatabase targetDb)
        {
            //Find entries in slaveList not in masterList to delete
            IEnumerable<PwUuid> entriesNotInSlave = sourceDb.RootGroup.GetEntries(true).Where(pe => entries.IndexOf(pe) < 0).Select(x => x.Uuid);
            PwObjectList<PwEntry> slaveList = targetDb.RootGroup.GetEntries(true);
            List<PwEntry> toDelete = slaveList.Where(e => entriesNotInSlave.Contains(e.Uuid)).ToList();
            DeleteEntries(toDelete, targetDb);
        }

        private static void deleteClean(PwDatabase dB)
        {
            dB.DeleteDuplicateEntries(new NullStatusLogger());
            dB.DeleteEmptyGroups();
            dB.Save(new NullStatusLogger());
        }

        private static void DeleteEntries(List<PwEntry> entriesToDelete, PwDatabase dB)
        {
            if (entriesToDelete.Count() > 0)
            {
                List<PwGroup> groupsToCheck = new List<PwGroup>();
                foreach (PwEntry entry in entriesToDelete)
                {
                    PwGroup parentGroup = entry.ParentGroup;
                    groupsToCheck.Add(parentGroup);
                    parentGroup.Entries.Remove(entry);
                }
                deleteClean(dB);
            }
        }

        private static void DeleteEntry(PwEntry entryToDelete, PwDatabase dB)
        {
            List<PwGroup> groupsToCheck = new List<PwGroup>();
            if (entryToDelete != null)
            {
                PwGroup parentGroup = entryToDelete.ParentGroup;
                groupsToCheck.Add(parentGroup);
                parentGroup.Entries.Remove(entryToDelete);
            }
        }
    }
}