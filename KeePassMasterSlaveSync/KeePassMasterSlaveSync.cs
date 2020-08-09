using System;
using System.Drawing;
using KeePass.Plugins;
using KeePass.Forms;
using System.Collections.Generic;
using KeePassLib.Utility;

namespace KeePassMasterSlaveSync
{
    public class KeePassMasterSlaveSyncExt : Plugin
    {
        private IPluginHost m_host = null;
        private List<Guid> SavingEvents = new List<Guid>();
        public override Image SmallIcon
        {
            get { return Resources.Icon16x16; }
        }

        public override string UpdateUrl
        {
            get { return "https://github.com/Angelelz/KeePassMasterSlaveSync/raw/master/keepass.version"; }
        }

        public override bool Initialize(IPluginHost host)
        {
            if (host == null) return false;
            m_host = host;
            m_host.MainWindow.FileSavingPre += StartSave;
            m_host.MainWindow.FileSaved += StartSync;

            return true;
        }

        public override void Terminate()
        {
            if (m_host != null)
            {
                m_host.MainWindow.FileSaved -= StartSync;
            }
        }

        private void StartSave(object sender, FileSavingEventArgs args)
        {
            SavingEvents.Add(args.EventGuid);
        }

        private void StartSync(object sender, FileSavedEventArgs args)
        {
            SavingEvents.Remove(args.EventGuid);
            if(SavingEvents.Count == 0)
            {
                Sync.StartSync(args.Database);
            }
        }

    }
}
