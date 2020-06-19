using System.Windows.Forms;

namespace LivePluginLoad {
    class Browse {
        public delegate void FileSelectedCallback(DialogResult result, string filePath);

        private FileSelectedCallback callback;

        public Browse(FileSelectedCallback callback) {
            this.callback = callback;
        }

        public void BrowseDLL() {
            using var ofd = new OpenFileDialog {Filter = "Dalamud Plugin|*.dll"};
            callback(ofd.ShowDialog(null), ofd.FileName);
        }
    }
}
