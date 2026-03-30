using NoDriver.Core.Runtime;
using System.IO.Compression;

namespace Test
{
    [TestClass]
    public class ConfigTests
    {
        private Config? _config = null;

        [TestInitialize]
        public void Setup()
        {
            _config = new Config();
        }

        [TestMethod]
        public void Constructor_Default_SetsRequiredProperties()
        {
            // Act
            var config = new Config();

            // Assert
            Assert.IsFalse(string.IsNullOrWhiteSpace(config.UserDataDir), "預設應產生暫存的 UserDataDir");
            Assert.IsFalse(config.CustomDataDir, "預設不應被標記為 CustomDataDir");
            Assert.IsFalse(string.IsNullOrWhiteSpace(config.Lang), "應自動抓取系統語系");
            Assert.IsFalse(string.IsNullOrWhiteSpace(config.BrowserExecutablePath), "應自動尋找 Chrome 執行檔路徑");
        }

        [TestMethod]
        public void UserDataDir_SetCustomPath_UpdatesCustomDataDirFlag()
        {
            // Act
            var customPath = @"C:\MyCustomChromeProfile";
            _config!.UserDataDir = customPath;

            // Assert
            Assert.AreEqual(customPath, _config.UserDataDir);
            Assert.IsTrue(_config.CustomDataDir, "設定自訂路徑後，CustomDataDir 應為 true");
        }

        [TestMethod]
        public void AddArgument_ValidArgument_AddedSuccessfully()
        {
            // Act
            var myArg = "--window-size=1920,1080";
            _config!.AddArgument(myArg);
            var args = _config.GetArgs();

            // Assert
            Assert.IsTrue(args.Contains(myArg), "合法的參數應該被加入到 Args 列表中");
        }

        [TestMethod]
        public void AddArgument_ForbiddenArgument_ThrowsArgumentException()
        {
            // Act & Assert
            Assert.ThrowsException<ArgumentException>(() => _config!.AddArgument("headless"));
            Assert.ThrowsException<ArgumentException>(() => _config!.AddArgument("--data-dir=C:\\"));
            Assert.ThrowsException<ArgumentException>(() => _config!.AddArgument("no-sandbox"));
            Assert.ThrowsException<ArgumentException>(() => _config!.AddArgument("lang=zh-TW"));
        }

        [TestMethod]
        public void AddExtension_NonExistentPath_ThrowsFileNotFoundException()
        {
            // Act & Assert
            Assert.ThrowsException<FileNotFoundException>(() => _config!.AddExtension("fake_path_that_does_not_exist"));
        }

        [TestMethod]
        public void AddExtension_ValidDirectoryWithManifest_AddsToExtensions()
        {
            // Arrange
            var testDir = Path.Combine(AppContext.BaseDirectory, $"test_ext_{Guid.NewGuid()}");
            Directory.CreateDirectory(testDir);

            // 建立一個假的 manifest.json 來騙過檢查
            File.WriteAllText(Path.Combine(testDir, "manifest.json"), "{}");

            // Act
            _config!.AddExtension(testDir);
            var args = _config.GetArgs();

            // Assert
            Assert.IsTrue(args.Any(it => it.StartsWith("--load-extension=") && it.Contains(testDir)), "應成功載入資料夾擴充功能");
            Assert.IsTrue(args.Contains("--enable-unsafe-extension-debugging"), "載入擴充功能後應開啟 unsafe-extension-debugging");

            // Cleanup
            if (Directory.Exists(testDir))
                Directory.Delete(testDir, true);
        }

        [TestMethod]
        public async Task AddExtension_ValidZipFile_ExtractsAndAddsToExtensions()
        {
            // Arrange
            var zipPath = Path.Combine(AppContext.BaseDirectory, $"test_ext_{Guid.NewGuid()}.zip");
            var sourceDir = Path.Combine(AppContext.BaseDirectory, $"test_zip_src_{Guid.NewGuid()}");
            Directory.CreateDirectory(sourceDir);

            // 產生要壓縮的內容
            File.WriteAllText(Path.Combine(sourceDir, "manifest.json"), "{}");
            ZipFile.CreateFromDirectory(sourceDir, zipPath);

            // Act & Assert
            _config!.AddExtension(zipPath);
            var args = _config.GetArgs();
            var loadArg = args.FirstOrDefault(it => it.StartsWith("--load-extension="));
            Assert.IsNotNull(loadArg, "應產生 --load-extension 參數");

            // 確保 ZIP 有被解壓縮到暫存資料夾
            var tempDir = loadArg.Split('=').Last();
            Assert.IsTrue(Directory.Exists(tempDir), "ZIP 檔應該被解壓縮到暫存目錄");

            // Cleanup
            if (Directory.Exists(sourceDir)) 
                Directory.Delete(sourceDir, true);

            if (File.Exists(zipPath)) 
                File.Delete(zipPath);

            // 呼叫本身的方法來清理產生的暫存解壓縮檔
            await ClearAsync();
        }

        [TestMethod]
        public void GetArgs_CheckFlags_AppliesConfigCorrectly()
        {
            // Arrange
            var config = new Config
            {
                Headless = true,
                Sandbox = false,
                Host = "127.0.0.1",
                Port = 9222,
                Expert = true
            };

            // Act
            var args = config.GetArgs();

            // Assert
            Assert.IsTrue(args.Contains("--headless=new"), "應包含 headless 參數");
            Assert.IsTrue(args.Contains("--no-sandbox"), "應包含 no-sandbox 參數");
            Assert.IsTrue(args.Contains("--remote-debugging-host=127.0.0.1"), "應包含 Host 參數");
            Assert.IsTrue(args.Contains("--remote-debugging-port=9222"), "應包含 Port 參數");
            Assert.IsTrue(args.Contains("--disable-site-isolation-trials"), "Expert 模式應包含 site-isolation-trials 參數");
            Assert.IsTrue(args.Contains($"--user-data-dir={config.UserDataDir}"), "應包含正確的 UserDataDir 參數");
        }

        [TestMethod]
        public async Task ClearAsync_RemovesTempExtensionDirectories()
        {
            // Arrange
            var zipPath = Path.Combine(AppContext.BaseDirectory, $"test_ext_{Guid.NewGuid()}.zip");
            var sourceDir = Path.Combine(AppContext.BaseDirectory, $"test_zip_src_{Guid.NewGuid()}");

            Directory.CreateDirectory(sourceDir);
            File.WriteAllText(Path.Combine(sourceDir, "manifest.json"), "{}");
            ZipFile.CreateFromDirectory(sourceDir, zipPath);

            _config!.AddExtension(zipPath);
            var args = _config.GetArgs();
            var loadArg = args.First(a => a.StartsWith("--load-extension="));
            var tempDir = loadArg.Split('=').Last();
            Assert.IsTrue(Directory.Exists(tempDir), "清理前，解壓縮的暫存資料夾應該存在");

            // Act
            await ClearAsync();

            // Assert
            Assert.IsFalse(Directory.Exists(tempDir), "執行後，解壓縮的暫存資料夾應該被刪除");

            // Cleanup
            if (Directory.Exists(sourceDir))
                Directory.Delete(sourceDir, true);

            if (File.Exists(zipPath))
                File.Delete(zipPath);
        }

        private async Task ClearAsync()
        {
            if (_config != null)
            {
                var tempExtensionDirs = _config.TempExtensionDirs.ToList();

                foreach (var tempDir in tempExtensionDirs)
                {
                    if (!string.IsNullOrWhiteSpace(tempDir))
                    {
                        for (var i = 0; i < 5; i++)
                        {
                            try
                            {
                                if (Directory.Exists(tempDir))
                                    Directory.Delete(tempDir, true);
                                Console.WriteLine($"Successfully removed temp extension dir {tempDir}");
                                break;
                            }
                            catch (Exception ex)
                            {
                                if (i == 4)
                                    Console.WriteLine(
                                        $"Problem removing temp extension dir {tempDir}\n" +
                                        $"Consider checking whether it's there and remove it by hand\n" +
                                        $"Error: {ex.Message}");
                                else
                                    await Task.Delay(150);
                            }
                        }
                    }
                }
            }
        }
    }
}
