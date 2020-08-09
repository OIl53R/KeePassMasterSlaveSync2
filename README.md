# KeePassMasterSlaveSync
[![Latest release](https://img.shields.io/github/release/OIl53R/KeePassMasterSlaveSync2.svg?label=latest%20release)](https://github.com/OIl53R/KeePassMasterSlaveSync2/releases/latest)
[![Github All Releases](https://img.shields.io/github/downloads/OIl53R/KeePassMasterSlaveSync2/total.svg)](https://github.com/OIl53R/KeePassMasterSlaveSync2/releases)
[![License](https://img.shields.io/github/license/OIl53R/KeePassMasterSlaveSync2.svg)](https://github.com/OIl53R/KeePassMasterSlaveSync2/blob/master/LICENSE)

KeePassMasterSlaveSync is a KeePass 2 plugin that Allows synchronization of specific Groups or Tags between local databases.\
This plugin is heavily based on [KeePassSubsetExport.](https://github.com/lukeIam/KeePassSubsetExport)\
THIS IS JUST A FURTHER DEVELOPMENT, SO THE MAIN CREDIT GOES TO [ANGELELZ KeePassMasterSlaveSync Project!](https://github.com/Angelelz/KeePassMasterSlaveSync)

## Why?
Automatically and securely share entries and groups with other databases. I have my personal database from which I share a group containing bank and family entries with my wife's database.
My Database act as a Master for those entries: If I delete or move any entry, It will be deleted from the Slave; but if there is different data in any entry, the one with the newer modification time will be synced across both databases.
Also, my wife's database (slave Database) can have entries to share too, for which it will be the master.

## Disclaimer
I'm not an expert programmer and I tried not to compromise security - but I can't guarantee it.  
**So use this plugin at your own risk.**  
If you have more experience with KeePass plugins, I would be very grateful if you have a look on the code.

## How to install?
- Download the latest release from [here](https://github.com/OIl53R/KeePassMasterSlaveSync2/releases)
- Place KeePassMasterSlaveSync.plgx in the KeePass program directory
- Start KeePass and the plugin is automatically loaded (check the Plugin menu)

## How to use?
- Open the database containing the entries that should be exported/synced
- Create a folder `MSSyncJobs` under the root folder
- For each export job (slave database) create a new entry:

| Setting                                                   | Description                                                             | Optional                                   | Example                                 |
| --------------------------------------------------------- | ----------------------------------------------------------------------- | ------------------------------------------ | --------------------------------------- |
| `Title`                                                   | Name of the job                                                         | No                                         | `MSS_MobilePhone`           |
| `Password`                                                | The password for the target database                                    | Yes, if `MSS_KeyFilePath` is set  | `SecurePW!`                             |
| `Expires`                                                 | If the entry expires the job is disabled and won't be executed          | `-`                                        | `-`                                     |
| `MSS_KeyFilePath`<br>[string field]           | Path to a key file                                                      | Yes, if `Password` is set                  | `C:\keys\mobile.key`                    |
| `MSS_TargetFilePath`<br>[string field]        | Path to the target database.<br>(Absolute, or relative to source database parent folder.) | No                       | `C:\sync\mobile.kdbx`<br>or<br>`mobile.kdbx`<br>or<br>`..\mobile.kdbx` |
| `MSS_Group`<br>[string field]                 | Group(s) for filtering (`,` to delimit multiple groups - `,` is not allowed in group names). Please don't use unnecessary Whitespaces before and after `,`, because these are added to the Group Names. Please be adviced that if you have multiple groups with the same name, all these folders will be shared with the slave. To prevent this use `/` to describe a group with a full path from your root group. Example: You have a Database `TestDB` and multiple groups: `TestDB/AllCustomers/Shared`,`TestDB/Customer1/Shared`,`TestDB/Customer2/Shared`,etc. You only want to share `TestDB/AllCustomers/Shared` and `TestDB/Customer1/Share` with the Slave Database from Customer1. To achieve this you should add `AllCustomers/Shared,Customer1/Shared` to the field.| Yes, if `MSS_Tag` is set          | `Shared` or `AllCustomers/Shared,Customer1/Shared` or `AllCustomers,Customer1`|
| `MSS_Tag`<br>[string field]                   | Tag(s) for filtering (`,` to delimit multiple tags - `,` is not allowed in tag names)| Yes, if `MSS_Group` is set        | `MobileSync`                            |
| `MSS_ExportUserAndPassOnly`<br>[string field]    | If `True` Only the Title, Url, Username and Password will be synced with the slave Database. | Yes (defaults to `False`) | `True`                             |
| `MSS_PerformSlaveJobs`<br>[string field]    | If true, Sync jobs on slave database will be executed too (Making it the master for those jobs). | Yes (defaults to `True`) | `True`                             |
| `MSS_IsSlave`<br>[string field]    | If `True` this job will be ignored when not executed from a Master database. This option prevents the warning "Missing Password or valid KeyFilePath" to show | Yes (defaults to `False`). `MSS_PerformSlaveJobs` must be `true` | `True`                             |

- Every time the (Master) database is saved, every configured sync job will be executed
- To disable an sync job temporarily just set it to expire, it does not matter the time
- If both `MSS_Group` and `MSS_Tag` are set, only entries matching *both* will be exported
- You can have a sync job on a slave database to target the Master database without setting a password or a key file, by executing from the master and setting 'MSS_IsSlave' to true in slave and 'MSS_PerformSlaveJobs' to true on the master.
- The plugin will automatically update the UI of any opened database.
- To prevent duplicated Uuids, the plugin will delete any entry from the slave DB that has been moved out of the synced group or tag.

## Changes OlI53R to [Angelelz Project](https://github.com/Angelelz/KeePassMasterSlaveSync/)
- Linebreaks are now allowed inside Settings
- Additional Delimiter (`/`) for full paths to groups
- Added immediate delete on slave side. So if delete something on the master (put to `Recycle Bin`), the entry will be deleted on the slave database
- Fixed issue with moving Folders
- Fixed Group not found when pushed `Save All` in KeyPass.
- Changed error Message for Group-Setting so you can see which group wasn't found on an error.

![create](https://raw.githubusercontent.com/OIl53R/KeePassMasterSlaveSync2/master/KeePassMasterSlaveSync/Capture/CaptureMSS.png)

## Next up!
- You tell me...

