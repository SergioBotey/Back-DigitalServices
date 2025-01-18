using System.Collections.Generic;

namespace digital_services.Objects.App
{
    public class FolderStructure
    {
        public List<Folder> Folders { get; set; }
    }

    public class Folder
    {
        public string Name { get; set; }
        public string NewName { get; set; }
        public List<FileAction> Files { get; set; }
    }

    public class FileAction
    {
        public string Name { get; set; }
        public string NewName { get; set; }
        public bool MoveToParent { get; set; }
    }
}