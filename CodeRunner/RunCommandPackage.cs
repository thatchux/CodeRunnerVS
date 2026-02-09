//------------------------------------------------------------------------------
// <copyright file="RunCommandPackage.cs" company="Company">
//     Copyright (c) Company.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------
 
using System;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.Win32;
 
namespace CodeRunner
{
    /// <summary>
    /// This is the class that implements the package exposed by this assembly.
    /// </summary>
    [PackageRegistration(UseManagedResourcesOnly = true)]
    [InstalledProductRegistration("#110", "#112", "1.0", IconResourceID = 400)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    // Register Tools->Options page
    [ProvideOptionPage(typeof(RunOptionsPage), "Code Runner", "General", 0, 0, true)]
    [Guid(RunCommandPackage.PackageGuidString)]
    [SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1650:ElementDocumentationMustBeSpelledCorrectly", Justification = "pkgdef, VS and vsixmanifest are valid VS terms")]
    public sealed class RunCommandPackage : Package
    {
        public const string PackageGuidString = "c4a9522f-53d1-478b-9bd2-47d51e5c92be";

        public RunCommandPackage()
        {
            // Initialization that does not require VS services goes here.
        }

        #region Package Members

        protected override void Initialize()
        {
            RunCommand.Initialize(this);
            base.Initialize();
        }

        #endregion
    }
}
