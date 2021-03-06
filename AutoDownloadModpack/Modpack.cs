﻿using System;
using System.Collections.Generic;

namespace AutoDownloadModpack
{
    [Serializable] 
    [ToString]
    public class RemoteFileList {
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "Used in JSON.")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE1006:Naming Styles", Justification = "From JSON")]
        public Dictionary<string, string> fileList {get; set;}

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "Used in JSON.")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE1006:Naming Styles", Justification = "From JSON")]
        public List<string> deleteFileList { get; set; }
    }
}