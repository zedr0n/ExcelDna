using System;
using System.Collections;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Build.Framework;
using ExcelDna.AddIn.Tasks.Utils;
using Microsoft.Build.Utilities;

namespace ExcelDna.AddIn.Tasks
{
    public class CreateExcelAddIn : AbstractTask
    {
        private readonly IExcelDnaFileSystem _fileSystem;
        private ITaskItem[] _configFilesInProject;
        private List<ITaskItem> _dnaFilesToPack;

        public CreateExcelAddIn()
            : this(new ExcelDnaPhysicalFileSystem())
        {
        }

        public CreateExcelAddIn(IExcelDnaFileSystem fileSystem)
        {
            if (fileSystem == null)
            {
                throw new ArgumentNullException("fileSystem");
            }

            _fileSystem = fileSystem;
        }

        public override bool Execute()
        {
            try
            {
                LogDiagnostics();

                RunSanityChecks();

                _dnaFilesToPack = new List<ITaskItem>();
                DnaFilesToPack = new ITaskItem[0];

                FilesInProject = FilesInProject ?? new ITaskItem[0];
                LogMessage("Number of files in project: " + FilesInProject.Length, MessageImportance.Low);

                _configFilesInProject = GetConfigFilesInProject();

                var buildItemsForDnaFiles = GetBuildItemsForDnaFiles();

                TryBuildAddInFor32Bit(buildItemsForDnaFiles);

                LogMessage("---");

                TryBuildAddInFor64Bit(buildItemsForDnaFiles);

                DnaFilesToPack = _dnaFilesToPack.ToArray();

                return true;
            }
            catch (Exception ex)
            {
                LogError("DNA" + ex.GetType().Name.GetHashCode(), ex.Message);
                LogError("DNA" + ex.GetType().Name.GetHashCode(), ex.ToString());
                return false;
            }
        }

        private void LogDiagnostics()
        {
            LogMessage("----Arguments----", MessageImportance.Low);
            LogMessage("FilesInProject: " + (FilesInProject ?? new ITaskItem[0]).Length, MessageImportance.Low);
            LogMessage("OutDirectory: " + OutDirectory, MessageImportance.Low);
            LogMessage("Xll32FilePath: " + Xll32FilePath, MessageImportance.Low);
            LogMessage("Xll64FilePath: " + Xll64FilePath, MessageImportance.Low);
            LogMessage("Create32BitAddIn: " + Create32BitAddIn, MessageImportance.Low);
            LogMessage("Create64BitAddIn: " + Create64BitAddIn, MessageImportance.Low);
            LogMessage("FileSuffix32Bit: " + FileSuffix32Bit, MessageImportance.Low);
            LogMessage("FileSuffix64Bit: " + FileSuffix64Bit, MessageImportance.Low);
            LogMessage("-----------------", MessageImportance.Low);
        }

        private void RunSanityChecks()
        {
            if (!_fileSystem.FileExists(Xll32FilePath))
            {
                throw new InvalidOperationException("File does not exist (Xll32FilePath): " + Xll32FilePath);
            }

            if (!_fileSystem.FileExists(Xll64FilePath))
            {
                throw new InvalidOperationException("File does not exist (Xll64FilePath): " + Xll64FilePath);
            }

            if (string.Equals(FileSuffix32Bit, FileSuffix64Bit, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("32-bit add-in suffix and 64-bit add-in suffix cannot be identical");
            }
        }

        private ITaskItem[] GetConfigFilesInProject()
        {
            var configFilesInProject = FilesInProject
                .Where(file => string.Equals(Path.GetExtension(file.ItemSpec), ".config", StringComparison.OrdinalIgnoreCase))
                .OrderBy(file => file.ItemSpec)
                .ToArray();

            return configFilesInProject;
        }

        private BuildItemSpec[] GetBuildItemsForDnaFiles()
        {
            var buildItemsForDnaFiles = (
                from item in FilesInProject
                where string.Equals(Path.GetExtension(item.ItemSpec), ".dna", StringComparison.OrdinalIgnoreCase)
                orderby item.ItemSpec
                let inputDnaFileNameAs32Bit = GetDnaFileNameAs32Bit(item.ItemSpec)
                let inputDnaFileNameAs64Bit = GetDnaFileNameAs64Bit(item.ItemSpec)
                select new BuildItemSpec
                {
                    InputDnaFileName = item.ItemSpec,

                    InputDnaFileNameAs32Bit = inputDnaFileNameAs32Bit,
                    InputDnaFileNameAs64Bit = inputDnaFileNameAs64Bit,

                    InputConfigFileNameAs32Bit = Path.ChangeExtension(inputDnaFileNameAs32Bit, ".config"),
                    InputConfigFileNameFallbackAs32Bit = GetAppConfigFileNameAs32Bit(),

                    InputConfigFileNameAs64Bit = Path.ChangeExtension(inputDnaFileNameAs64Bit, ".config"),
                    InputConfigFileNameFallbackAs64Bit = GetAppConfigFileNameAs64Bit(),

                    OutputDnaFileNameAs32Bit = Path.Combine(OutDirectory, inputDnaFileNameAs32Bit),
                    OutputDnaFileNameAs64Bit = Path.Combine(OutDirectory, inputDnaFileNameAs64Bit),

                    OutputXllFileNameAs32Bit = Path.Combine(OutDirectory, Path.ChangeExtension(inputDnaFileNameAs32Bit, ".xll")),
                    OutputXllFileNameAs64Bit = Path.Combine(OutDirectory, Path.ChangeExtension(inputDnaFileNameAs64Bit, ".xll")),

                    OutputConfigFileNameAs32Bit = Path.Combine(OutDirectory, Path.ChangeExtension(inputDnaFileNameAs32Bit, ".xll.config")),
                    OutputConfigFileNameAs64Bit = Path.Combine(OutDirectory, Path.ChangeExtension(inputDnaFileNameAs64Bit, ".xll.config")),
                }).ToArray();

            return buildItemsForDnaFiles;
        }

        private void TryBuildAddInFor32Bit(BuildItemSpec[] buildItemsForDnaFiles)
        {
            foreach (var item in buildItemsForDnaFiles)
            {
                if (Create32BitAddIn && ShouldCopy32BitDnaOutput(item, buildItemsForDnaFiles))
                {
                    // Copy .dna file to build output folder for 32-bit
                    CopyFileToBuildOutput(item.InputDnaFileName, item.OutputDnaFileNameAs32Bit, overwrite: true);

                    // Copy .xll file to build output folder for 32-bit
                    CopyFileToBuildOutput(Xll32FilePath, item.OutputXllFileNameAs32Bit, overwrite: true);

                    // Copy .config file to build output folder for 32-bit (if exist)
                    TryCopyConfigFileToOutput(item.InputConfigFileNameAs32Bit, item.InputConfigFileNameFallbackAs32Bit, item.OutputConfigFileNameAs32Bit);

                    AddDnaToListOfFilesToPack(item.OutputDnaFileNameAs32Bit, item.OutputXllFileNameAs32Bit, item.OutputConfigFileNameAs32Bit);
                }
            }
        }

        private void TryBuildAddInFor64Bit(BuildItemSpec[] buildItemsForDnaFiles)
        {
            foreach (var item in buildItemsForDnaFiles)
            {
                if (Create64BitAddIn && ShouldCopy64BitDnaOutput(item, buildItemsForDnaFiles))
                {
                    // Copy .dna file to build output folder for 64-bit
                    CopyFileToBuildOutput(item.InputDnaFileName, item.OutputDnaFileNameAs64Bit, overwrite: true);

                    // Copy .xll file to build output folder for 64-bit
                    CopyFileToBuildOutput(Xll64FilePath, item.OutputXllFileNameAs64Bit, overwrite: true);

                    // Copy .config file to build output folder for 64-bit (if exist)
                    TryCopyConfigFileToOutput(item.InputConfigFileNameAs64Bit, item.InputConfigFileNameFallbackAs64Bit, item.OutputConfigFileNameAs64Bit);

                    AddDnaToListOfFilesToPack(item.OutputDnaFileNameAs64Bit, item.OutputXllFileNameAs64Bit, item.OutputConfigFileNameAs64Bit);
                }
            }
        }

        private string GetDnaFileNameAs32Bit(string fileName)
        {
            return GetFileNameWithBitnessSuffix(fileName, FileSuffix32Bit);
        }

        private string GetDnaFileNameAs64Bit(string fileName)
        {
            return GetFileNameWithBitnessSuffix(fileName, FileSuffix64Bit);
        }

        private string GetAppConfigFileNameAs32Bit()
        {
            return GetFileNameWithBitnessSuffix("App.config", FileSuffix32Bit);
        }

        private string GetAppConfigFileNameAs64Bit()
        {
            return GetFileNameWithBitnessSuffix("App.config", FileSuffix64Bit);
        }

        private string GetFileNameWithBitnessSuffix(string fileName, string suffix)
        {
            var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fileName) ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(FileSuffix32Bit))
            {
                var indexOfSuffix = fileNameWithoutExtension.LastIndexOf(FileSuffix32Bit, StringComparison.OrdinalIgnoreCase);
                if (indexOfSuffix > 0)
                {
                    fileNameWithoutExtension = fileNameWithoutExtension.Remove(indexOfSuffix);
                }
            }

            if (!string.IsNullOrWhiteSpace(FileSuffix64Bit))
            {
                var indexOfSuffix = fileNameWithoutExtension.LastIndexOf(FileSuffix64Bit, StringComparison.OrdinalIgnoreCase);
                if (indexOfSuffix > 0)
                {
                    fileNameWithoutExtension = fileNameWithoutExtension.Remove(indexOfSuffix);
                }
            }

            var extension = Path.GetExtension(fileName);

            return Path.Combine(Path.GetDirectoryName(fileName) ?? string.Empty, fileNameWithoutExtension + suffix + extension);
        }

        private static bool ShouldCopy32BitDnaOutput(BuildItemSpec item, IEnumerable<BuildItemSpec> buildItems)
        {
            if (item.InputDnaFileName.Equals(item.InputDnaFileNameAs32Bit))
            {
                return true;
            }

            var specificFileExists = buildItems
                .Any(bi => item.InputDnaFileNameAs32Bit.Equals(bi.InputDnaFileName, StringComparison.OrdinalIgnoreCase));

            return !specificFileExists;
        }

        private static bool ShouldCopy64BitDnaOutput(BuildItemSpec item, IEnumerable<BuildItemSpec> buildItems)
        {
            if (item.InputDnaFileName.Equals(item.InputDnaFileNameAs64Bit))
            {
                return true;
            }

            var specificFileExists = buildItems
                .Any(bi => item.InputDnaFileNameAs64Bit.Equals(bi.InputDnaFileName, StringComparison.OrdinalIgnoreCase));

            return !specificFileExists;
        }

        private void TryCopyConfigFileToOutput(string inputConfigFile, string inputFallbackConfigFile, string outputConfigFile)
        {
            var configFile = TryFindAppConfigFileName(inputConfigFile, inputFallbackConfigFile);
            if (!string.IsNullOrWhiteSpace(configFile))
            {
                CopyFileToBuildOutput(configFile, outputConfigFile, overwrite: true);
            }
        }

        private string TryFindAppConfigFileName(string preferredConfigFileName, string fallbackConfigFileName)
        {
            if (_configFilesInProject.Any(c => c.ItemSpec.Equals(preferredConfigFileName, StringComparison.OrdinalIgnoreCase)))
            {
                return preferredConfigFileName;
            }

            if (_configFilesInProject.Any(c => c.ItemSpec.Equals(fallbackConfigFileName, StringComparison.OrdinalIgnoreCase)))
            {
                return fallbackConfigFileName;

            }

            var appConfigFile = _configFilesInProject.FirstOrDefault(c => c.ItemSpec.Equals("App.config", StringComparison.OrdinalIgnoreCase));
            if (appConfigFile != null)
            {
                return appConfigFile.ItemSpec;
            }

            return null;
        }

        private void CopyFileToBuildOutput(string sourceFile, string destinationFile, bool overwrite)
        {
            LogMessage(_fileSystem.GetRelativePath(sourceFile) + " -> " + _fileSystem.GetRelativePath(destinationFile));

            var destinationFolder = Path.GetDirectoryName(destinationFile);
            if (!string.IsNullOrWhiteSpace(destinationFolder) && !_fileSystem.DirectoryExists(destinationFolder))
            {
                _fileSystem.CreateDirectory(destinationFolder);
            }

            _fileSystem.CopyFile(sourceFile, destinationFile, overwrite);
        }

        private void AddDnaToListOfFilesToPack(string outputDnaFileName, string outputXllFileName, string outputXllConfigFileName)
        {
            if (!PackIsEnabled)
            {
                return;
            }

            var outputPackedXllFileName = !string.IsNullOrWhiteSpace(PackedFileSuffix)
                ? Path.Combine(Path.GetDirectoryName(outputXllFileName) ?? string.Empty,
                    Path.GetFileNameWithoutExtension(outputXllFileName) + PackedFileSuffix + ".xll")
                : outputXllFileName;

            var metadata = new Hashtable
            {
                {"OutputDnaFileName", outputDnaFileName},
                {"OutputPackedXllFileName", outputPackedXllFileName},
                {"OutputXllConfigFileName", outputXllConfigFileName },
            };

            _dnaFilesToPack.Add(new TaskItem(outputDnaFileName, metadata));
        }

        /// <summary>
        /// The list of files in the project marked as Content or None
        /// </summary>
        [Required]
        public ITaskItem[] FilesInProject { get; set; }

        /// <summary>
        /// The directory in which the built files were written to
        /// </summary>
        [Required]
        public string OutDirectory { get; set; }

        /// <summary>
        /// The 32-bit .xll file path; set to <code>$(MSBuildThisFileDirectory)\ExcelDna.xll</code> by default
        /// </summary>
        [Required]
        public string Xll32FilePath { get; set; }

        /// <summary>
        /// The 64-bit .xll file path; set to <code>$(MSBuildThisFileDirectory)\ExcelDna64.xll</code> by default
        /// </summary>
        [Required]
        public string Xll64FilePath { get; set; }

        /// <summary>
        /// Enable/disable building 32-bit .dna files
        /// </summary>
        public bool Create32BitAddIn { get; set; }

        /// <summary>
        /// Enable/disable building 64-bit .dna files
        /// </summary>
        public bool Create64BitAddIn { get; set; }

        /// <summary>
        /// The name suffix for 32-bit .dna files
        /// </summary>
        public string FileSuffix32Bit { get; set; }

        /// <summary>
        /// The name suffix for 64-bit .dna files
        /// </summary>
        public string FileSuffix64Bit { get; set; }

        /// <summary>
        /// Enable/disable running ExcelDnaPack for .dna files
        /// </summary>
        public bool PackIsEnabled { get; set; }

        /// <summary>
        /// Enable/disable running ExcelDnaPack for .dna files
        /// </summary>
        public string PackedFileSuffix { get; set; }

        /// <summary>
        /// The list of .dna files copied to the output
        /// </summary>
        [Output]
        public ITaskItem[] DnaFilesToPack { get; set; }
    }
}
