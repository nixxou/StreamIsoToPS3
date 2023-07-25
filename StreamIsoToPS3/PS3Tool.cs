using DiscUtils.Iso9660;
using StreamIsoToPS3;
using System.Diagnostics;
using System.Globalization;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Web;

namespace PS3IsoLauncher
{
	public class PS3Status
	{
		public bool Online = false;
		public bool GameRun = false;
		public bool Mounted = false;
	}
	public class PS3Tool
	{

		[DllImport("user32.dll")]
		static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

		[DllImport("user32.dll", SetLastError = true)]
		static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

		const int SW_HIDE = 0;

		public string IsoFilePath { get; set; }
		public string Ps3Ip { get; private set; }
		public bool MountPkg { get; private set; }

		public string TrueTitle { get; private set; }
		public string TitleID { get; private set; }
		public double AppVersion { get; private set; }
		public double FirmwareVersion { get; private set; }

		public bool HavePkgDir { get; private set; }

		public SortedDictionary<string, long> pkgList { get; private set; } = new SortedDictionary<string, long>();
		public SortedDictionary<string, long> dlcList { get; private set; } = new SortedDictionary<string, long>();
		public SortedDictionary<string, long> rapList { get; private set; } = new SortedDictionary<string, long>();


		private Dictionary<string, long> _fileList = new Dictionary<string, long>();
		private PARAM_SFO _paramSfo;

		public string PKGDIR { get; private set; } = "UPDATES_AND_DLC";

		public char IsoMountDrive { get; set; }

		public PS3Tool(string isoFilePath, string ps3ip, bool mountPkg)
		{
			IsoFilePath = isoFilePath;
			Ps3Ip = ps3ip;
			MountPkg = mountPkg;
			KillPS3Server();
			IsoMountDrive = GetIsoMountDrive();
			if (IsoMountDrive != '\0') Umount();

			try
			{
				using (FileStream fileStream = File.Open(IsoFilePath, FileMode.Open)) { }
			}
			catch (IOException)
			{
				throw new Exception($"The file {IsoFilePath} can't be open");
			}

			if (!IsPS3Iso()) throw new Exception("Invalid PS3 Iso");
			GetFileData();
		}

		public char GetIsoMountDrive()
		{
			string resultat = "";
			Task.Run(async () => { resultat = await ExecuteProcess($"$drive = (Get-DiskImage \"{IsoFilePath}\" | Get-Volume).DriveLetter;echo $drive"); }).Wait();
			string driveLetterString = resultat.Trim('\n').Trim('\r').Trim();
			if (driveLetterString.Length == 1) return driveLetterString.ToCharArray()[0];
			else return '\0';
		}

		public bool Mount()
		{
			string resultat = "";
			Task.Run(async () => { resultat = await ExecuteProcess($"Mount-DiskImage \"{IsoFilePath}\""); }).Wait();
			Thread.Sleep(500);
			IsoMountDrive = GetIsoMountDrive();
			if (IsoMountDrive == '\0') return false;
			else return true;
		}

		public bool Umount()
		{
			string resultat = "";
			Task.Run(async () => { resultat = await ExecuteProcess($"Dismount-DiskImage \"{IsoFilePath}\""); }).Wait();
			Thread.Sleep(500);
			IsoMountDrive = GetIsoMountDrive();
			if (IsoMountDrive == '\0') return true;
			else return false;
		}


		public void KillPS3Server()
		{
			Process[] procs = Process.GetProcessesByName("ps3netsrvisoplay");
			foreach (Process p in procs) { p.Kill(); }
		}
		public void WaitForGameQuit()
		{
			int nbBadPing = 0;
			var ps3State = GetPS3Status();
			while (ps3State.GameRun || !ps3State.Online)
			{
				Console.WriteLine($"GameStatus : {ps3State.GameRun}");
				Thread.Sleep(3000);
				if (!ps3State.Online)
				{
					if (!Ping())
					{
						nbBadPing = nbBadPing + 1;
					}
				}
				else
				{
					nbBadPing = 0;
				}
				if (nbBadPing > 5)
				{
					Console.WriteLine("No Ping, Force Exit");
					break;
				}
				ps3State = GetPS3Status();
			}
		}

		public bool PS3LaunchGame()
		{
			string urlencodedFile = HttpUtility.UrlEncode(Path.GetFileName(IsoFilePath));
			string urllaunch = $"http://{Ps3Ip}/mount.ps3/net0/PS3ISO/{urlencodedFile}";
			Task.Run(async () => { await GetWebPageContentAsync(urllaunch); }).Wait();

			Console.WriteLine("Waiting For GameLaunch ");
			for (int i = 0; i < 20; i++)
			{
				Console.Write(".");
				Thread.Sleep(2000);
				var ps3State = GetPS3Status();
				if (ps3State.GameRun) return true;
			}
			return false;
		}

		public bool PS3LaunchPKG()
		{
			string urllaunch = $"http://{Ps3Ip}/mount.ps3/net0/PKG";
			Task.Run(async () => { await GetWebPageContentAsync(urllaunch); }).Wait();

			Console.WriteLine("Waiting For Mount ");
			for (int i = 0; i < 20; i++)
			{
				Console.Write(".");
				Thread.Sleep(2000);
				var ps3State = GetPS3Status();
				if (ps3State.Mounted) return true;
			}
			return false;
		}
		public async Task RunPS3Server2(string PS3ServerDir = "")
		{
			Console.WriteLine("ps3netsrvisoplay.exe Start");

			System.Diagnostics.ProcessStartInfo StartInfo = new System.Diagnostics.ProcessStartInfo
			{
				UseShellExecute = false, //<- for elevation
				CreateNoWindow = true,
				WorkingDirectory = @"C:\Users\Mehdi\source\repos\StreamIsoToPS3\StreamIsoToPS3\bin\Debug\net6.0\ps3serv",
				FileName = @"C:\Users\Mehdi\source\repos\StreamIsoToPS3\StreamIsoToPS3\bin\Debug\net6.0\ps3serv\ps3netsrvisoplay.exe",
				Arguments = '\"' + PS3ServerDir + '\"'
			};
			System.Diagnostics.Process p = System.Diagnostics.Process.Start(StartInfo);
			if(p != null)
			{
				await p.WaitForExitAsync();
			}
			Console.WriteLine("ps3netsrvisoplay.exe Closed");
		}

		public string CreateIsoFolder()
		{
			string PS3ServerDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ps3serv");
			string IsoDir = Path.Combine(PS3ServerDir, "PS3ISO");
			if (!Directory.Exists(PS3ServerDir))
			{
				Directory.CreateDirectory(PS3ServerDir);
			}
			CreateMaps.JunctionPoint.Create(IsoDir, Path.GetDirectoryName(IsoFilePath), true);
			return PS3ServerDir;
		}

		public string CreatePkgFolder()
		{
			string PS3ServerDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ps3serv");
			string IsoDir = Path.Combine(PS3ServerDir, "PKG");
			if (!Directory.Exists(PS3ServerDir))
			{
				Directory.CreateDirectory(PS3ServerDir);
			}
			CreateMaps.JunctionPoint.Create(IsoDir, Path.Combine(IsoMountDrive + ":\\", PKGDIR), true);
			return PS3ServerDir;
		}


		public bool Ping()
		{
			bool resultping;
			using (Ping ping = new Ping())
			{
				try
				{

					PingReply reply = ping.Send(Ps3Ip);
					resultping = reply.Status == IPStatus.Success;
				}
				catch (PingException)
				{
					resultping = false;
				}
			}
			return resultping;
		}

		public PS3Status GetPS3Status()
		{
			var res = new PS3Status();
			string url = $"http://{Ps3Ip}/cpursx.ps3?/sman.ps3";
			string content = "";
			Task.Run(async () => { content = await GetWebPageContentAsync(url); }).Wait();
			if (!content.Contains("<title>wMAN MOD"))
			{
				return res;
			}
			res.Online = true;

			Regex regex = new Regex(@"pid=([0-9]*)");
			Match match = regex.Match(content);
			if (match.Success) res.GameRun = true;

			if (content.Contains("/dev_bdvd"))
			{
				res.Mounted = true;
			}

			return res;
		}

		public bool PS3QuitGame()
		{
			Task.Run(async () => { await GetWebPageContentAsync($"http://{Ps3Ip}/xmb.ps3$exit"); }).Wait();
			for (int i = 0; i < 15; i++)
			{
				Thread.Sleep(3000);
				var ps3State = GetPS3Status();
				if (!ps3State.GameRun && ps3State.Online) return true;
			}
			return false;
		}

		public bool PS3Umount()
		{
			Task.Run(async () => { await GetWebPageContentAsync($"http://{Ps3Ip}/mount.ps3/unmount"); }).Wait();
			for (int i = 0; i < 15; i++)
			{
				Thread.Sleep(3000);
				var ps3State = GetPS3Status();
				if (!ps3State.Mounted && ps3State.Online) return true;
			}
			return false;
		}

		private void GetFileData()
		{

			CDBuilder builder = new CDBuilder();
			using (FileStream fs = System.IO.File.Open(IsoFilePath, FileMode.Open))
			{

				CDReader cd = new CDReader(fs, true, true);
				var pkgDir = cd.Root.GetDirectories().Where(r => r.Name == PKGDIR).SingleOrDefault();

				if (pkgDir != null)
				{
					HavePkgDir = true;
					var pkglist = pkgDir.GetAllFiles();
					foreach (var pkg in pkglist)
					{
						if (pkg.Extension.ToLower() == "rap")
						{
							rapList.Add(pkg.Name, pkg.Length);
						}
					}
					foreach (var pkg in pkglist)
					{
						if (pkg.Extension.ToLower() == "pkg")
						{
							bool isDlc = false;
							foreach (var rap in rapList)
							{
								if (pkg.Name.ToLower().Contains(Path.GetFileNameWithoutExtension(rap.Key).ToLower()))
								{
									isDlc = true;
									break;
								}
							}
							if (isDlc)
							{
								dlcList.Add(pkg.Name, pkg.Length);
							}
							else
							{
								pkgList.Add(pkg.Name, pkg.Length);
							}

						}
					}
				}
			}
		}
		private bool IsPS3Iso()
		{
			CDBuilder builder = new CDBuilder();
			using (FileStream fs = System.IO.File.Open(IsoFilePath, FileMode.Open))
			{
				CDReader cd = new CDReader(fs, true, true);
				var ParamFile = cd.GetFileInfo("PS3_GAME\\PARAM.SFO");
				if (ParamFile.Exists)
				{
					try
					{
						_paramSfo = new PARAM_SFO(ParamFile.Open(FileMode.Open));
						TrueTitle = _paramSfo.Title.Replace("\r", "").Replace("\n", "").Replace("\r\n", "");
						TitleID = _paramSfo.TitleID.ToUpper();
						var firmwareVersionTxt = _paramSfo.Tables.SingleOrDefault(t => t.Name == "PS3_SYSTEM_VER").Value.TrimStart('0');
						AppVersion = TryParseVersion(_paramSfo.APP_VER);
						FirmwareVersion = TryParseVersion(firmwareVersionTxt);

					}
					catch
					{
						return false;
					}

					return true;
				}
			}

			return false;
		}

		public static double TryParseVersion(string text)
		{
			double res = 0;
			if (String.IsNullOrEmpty(text)) return res;

			text = text.TrimStart('0');
			if (Double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out res))
			{
				return res;
			}
			return 0;
		}
		private static async Task<string> GetWebPageContentAsync(string url)
		{
			using (HttpClient httpClient = new HttpClient())
			{
				try
				{
					// Faites une requête GET à l'URL spécifiée
					HttpResponseMessage response = await httpClient.GetAsync(url);

					// Vérifiez si la réponse est réussie (code 2xx)
					response.EnsureSuccessStatusCode();

					// Lisez le contenu de la réponse en tant que chaîne
					string content = await response.Content.ReadAsStringAsync();

					return content;
				}
				catch (HttpRequestException e)
				{
					//Console.WriteLine($"Erreur HTTP : {e.Message}");
					return "";
				}
			}
		}

		private async Task<string> ExecuteProcess(string message, bool returnerror = false)
		{
			message = message.Replace("\"", "\"\"\"");
			using (var app = new Process())
			{
				app.StartInfo.FileName = "powershell.exe";
				app.StartInfo.Arguments = message;
				app.EnableRaisingEvents = true;
				app.StartInfo.RedirectStandardOutput = true;
				app.StartInfo.RedirectStandardError = true;
				// Must not set true to execute PowerShell command
				app.StartInfo.UseShellExecute = false;

				app.StartInfo.CreateNoWindow = true;

				app.Start();

				using (var o = app.StandardError)
				{
					Console.WriteLine(await o.ReadToEndAsync());
				}

				if (returnerror)
				{
					using (var o = app.StandardError)
					{
						return await o.ReadToEndAsync();
					}
				}

				using (var o = app.StandardOutput)
				{
					return await o.ReadToEndAsync();
				}
			}
		}
	}
}
