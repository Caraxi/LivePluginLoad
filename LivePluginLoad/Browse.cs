using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LivePluginLoad {
    class Browse {
        public delegate void FileSelectedCallback(DialogResult result, string filePath);

        private FileSelectedCallback callback;

        public Browse(FileSelectedCallback callback) {
            this.callback = callback;
        }


        [STAThread]
        public void BrowseDLL() {
            using var ofd = new OpenFileDialog {Filter = "Dalamud Plugin|*.dll"};
            callback(ofd.ShowDialog(), ofd.FileName);
        }
    }
}
