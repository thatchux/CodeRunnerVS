using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using Microsoft.VisualStudio.Shell;

namespace CodeRunner
{
  public class RunOptionsPage : DialogPage
  {
    private string extensionMappings = @".js=node
.php=php
.py=python
.pl=perl
.rb=ruby
.go=go run
.lua=lua
.groovy=groovy
.scala=scala
.vbs=cscript //Nologo
.ps1=pwsh -NoProfile -ExecutionPolicy Bypass -File";

    [Category("Mappings")]
    [DisplayName("Extension mappings")]
    [Description("Per-line mappings in the format: .ext=command [args]\r\nExample: .py=python\r\nUse .ps1 to override PowerShell invocation.")]
    public string ExtensionMappings
    {
      get => extensionMappings;
      set => extensionMappings = value ?? string.Empty;
    }

    /// <summary>
    /// Parse the ExtensionMappings text into a dictionary (.ext => mapping).
    /// Lines starting with '#' or empty lines are ignored.
    /// </summary>
    public IDictionary<string, string> GetMappingsDictionary()
    {
      var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
      if (string.IsNullOrWhiteSpace(extensionMappings))
        return dict;

      using (var sr = new StringReader(extensionMappings))
      {
        string line;
        while ((line = sr.ReadLine()) != null)
        {
          var trimmed = line.Trim();
          if (trimmed.Length == 0 || trimmed.StartsWith("#"))
            continue;
          int i = trimmed.IndexOf('=');
          if (i <= 0)
            continue;
          var ext = trimmed.Substring(0, i).Trim();
          var mapping = trimmed.Substring(i + 1).Trim();
          if (!ext.StartsWith("."))
            ext = "." + ext;
          if (!string.IsNullOrEmpty(ext) && !string.IsNullOrEmpty(mapping))
            dict[ext] = mapping;
        }
      }

      return dict;
    }
  }
}