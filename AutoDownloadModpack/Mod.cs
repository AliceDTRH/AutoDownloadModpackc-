using System;

namespace AutoDownloadModpack
{
    public struct Mod
    {
        private string _path;
        public string Path {
            get {
                return mainPath + _path;
            }
            set {
                _path = value;
            }
        }
        public string DownloadUrl {
            get {
                return Program.host + _path;
            }
        }
        public string Etag { get; set; }

        private readonly string mainPath;

        public Mod(string mainPath) : this()
        {
            this.mainPath = mainPath;
            this.Purge = false;
        }

        public string Sha1 { get; set; }
        public bool Purge { get; internal set; }

        override public string ToString() { return "[" + Path + ", " + DownloadUrl + ", " + Etag + "]"; }
    }
}