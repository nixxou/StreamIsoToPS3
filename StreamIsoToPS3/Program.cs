using PS3IsoLauncher;
using System.Text.RegularExpressions;

internal class Program
{
	private static void Main(string[] args)
	{

		/*
		List<string>fakeArgs = new List<string>();
		fakeArgs.Add(@"H:\work\out\NBA Jam [BLUS30696].iso");
		fakeArgs.Add(@"192.168.1.130");
		args = fakeArgs.ToArray();
		*/

		int nbArgNeeded = 2;
		string argIso = "";
		string argIP = "";
		bool mountpkg = false;
		if (args.Contains("--mountpkg"))
		{
			nbArgNeeded = 3;
			mountpkg = true;
		}
		if (args.Length != nbArgNeeded)
		{
			ShowCommand();
			return;
		}
		else
		{
			argIso = args[0];
			argIP = args[1];
		}

		if (!argIso.ToLower().EndsWith(".iso"))
		{
			Console.WriteLine($"{argIso} is not an iso file");
			return;
		}
		if (!System.IO.File.Exists(argIso))
		{
			Console.WriteLine($"The file {argIso} does not exist");
			return;
		}
		string pattern = @"^(25[0-5]|2[0-4][0-9]|[0-1]?[0-9]{1,2})\.(25[0-5]|2[0-4][0-9]|[0-1]?[0-9]{1,2})\.(25[0-5]|2[0-4][0-9]|[0-1]?[0-9]{1,2})\.(25[0-5]|2[0-4][0-9]|[0-1]?[0-9]{1,2})$";
		if (!Regex.IsMatch(argIP, pattern))
		{
			Console.WriteLine($"{argIP} is not a valid IP");
			return;
		}

		PS3Tool ps3Tool;
		try
		{
			ps3Tool = new PS3Tool(argIso, argIP, mountpkg);
		}
		catch (Exception ex)
		{
			Console.Write(ex.ToString());
			return;
		}
		Console.WriteLine($"PS3 IP : {argIP}");
		Console.WriteLine($"Loading Iso : {argIso}");
		Console.WriteLine($"Name : {ps3Tool.TrueTitle}");
		Console.WriteLine($"TitleId : {ps3Tool.TitleID}");
		Console.WriteLine($"Firmware Version : {ps3Tool.FirmwareVersion}");
		Console.WriteLine($"App Version : {ps3Tool.AppVersion}");

		Console.WriteLine($"PKG number: {ps3Tool.pkgList.Count()}");
		foreach (var pkg in ps3Tool.pkgList)
		{
			Console.WriteLine($"{pkg.Key} \t\t[{HumanReadableFileSize(pkg.Value)}]");
		}
		Console.WriteLine();
		Console.WriteLine($"DLC number: {ps3Tool.dlcList.Count()}");
		foreach (var dlc in ps3Tool.dlcList)
		{
			Console.WriteLine($"{dlc.Key} \t\t[{HumanReadableFileSize(dlc.Value)}]");
		}
		Console.WriteLine();

		Console.WriteLine();

		if (!ps3Tool.Ping())
		{
			Console.WriteLine($"{argIP} don't ping");
			return;
		}
		Console.WriteLine($"PS3 PING : Ping OK on {argIP}");

		var onlineStatus = ps3Tool.GetPS3Status();
		Console.WriteLine($"PS3 Online : {onlineStatus.Online}");
		Console.WriteLine($"PS3 Process Runing : {onlineStatus.GameRun}");
		Console.WriteLine($"PS3 /dev_bdvd mounted : {onlineStatus.Mounted}");

		if (!onlineStatus.Online)
		{
			string url = $"http://{argIP}/cpursx.ps3?/sman.ps3";
			Console.WriteLine($"wMAN not answering on {url}");
			return;
		}

		Console.WriteLine();
		if (onlineStatus.GameRun)
		{
			Console.WriteLine("A Game is already launched, ask to quit");
			bool isGameExit = ps3Tool.PS3QuitGame();
			if (isGameExit)
			{
				Console.WriteLine("Game Exited");
			}
			else
			{
				Console.WriteLine($"Error, the game did not exit");
				return;
			}
		}

		Console.WriteLine();
		if (onlineStatus.Mounted)
		{
			Console.WriteLine("UnMount /dev_bdvd");
			bool isUnMount = ps3Tool.PS3Umount();
			if (isUnMount)
			{
				Console.WriteLine("/dev_bdvd UnMount");
			}
			else
			{
				Console.WriteLine($"Error, /dev_bdvd did not Unmount");
				return;
			}
		}

		if (mountpkg == false)
		{
			ps3Tool.KillPS3Server();

			string PS3ServerDir = ps3Tool.CreateIsoFolder();
			Console.WriteLine($"Link iso in {PS3ServerDir}");
			Thread.Sleep(1000);


			var TaskPS3Serv = System.Threading.Tasks.Task.Run(() =>
			ps3Tool.RunPS3Server2(PS3ServerDir)
			);
			//TaskPS3Serv.Wait();

			Thread.Sleep(3000);


			bool isGameLaunched = ps3Tool.PS3LaunchGame();

			if (!isGameLaunched)
			{
				Console.WriteLine("Error Launching the game, it will exit");
				ps3Tool.KillPS3Server();

			}
			else
			{
				Console.WriteLine();
				Console.WriteLine("The Game is ON ! Press any keys to Exit");

				Console.ReadLine();
				if (ps3Tool.Ping())
				{
					onlineStatus = ps3Tool.GetPS3Status();
					Console.WriteLine($"PS3 Online : {onlineStatus.Online}");
					Console.WriteLine($"PS3 Process Runing : {onlineStatus.GameRun}");
					Console.WriteLine($"PS3 /dev_bdvd mounted : {onlineStatus.Mounted}");


					if (!onlineStatus.Online)
					{
						string url = $"http://{argIP}/cpursx.ps3?/sman.ps3";
						Console.WriteLine($"wMAN not answering on {url}");
						return;
					}

					Console.WriteLine();
					if (onlineStatus.GameRun)
					{
						Console.WriteLine("A Game is already launched, ask to quit");
						bool isGameExit = ps3Tool.PS3QuitGame();
						if (isGameExit)
						{
							Console.WriteLine("Game Exited");
						}
						else
						{
							Console.WriteLine($"Error, the game did not exit");
							return;
						}
					}
				}
				ps3Tool.KillPS3Server();
			}
			TaskPS3Serv.Wait();
			Console.WriteLine("Done");

		}
		else
		{
			int nbpak = ps3Tool.pkgList.Count() + ps3Tool.dlcList.Count();
			if (nbpak == 0)
			{
				Console.WriteLine("No DLC or UPDATES inside this iso file");

			}
			else
			{
				string pkgdir = ps3Tool.PKGDIR;

				Console.WriteLine("Mount ISO");
				if (!ps3Tool.Mount())
				{
					Console.WriteLine("Error mounting iso, abording");
					return;
				}
				else
				{
					Console.WriteLine($"Iso mount on ${ps3Tool.IsoMountDrive}");
				}

				string PS3ServerDir = ps3Tool.CreatePkgFolder();
				Console.WriteLine($"Link pkg in {PS3ServerDir}");
				Thread.Sleep(1000);


				var TaskPS3Serv = System.Threading.Tasks.Task.Run(() =>
				ps3Tool.RunPS3Server2(PS3ServerDir)
				);
				Thread.Sleep(3000);

				bool isMounted = ps3Tool.PS3LaunchPKG();
				if (!isMounted)
				{
					Console.WriteLine("Error mounting pkg, it will exit");
					ps3Tool.KillPS3Server();

				}
				else
				{
					Console.WriteLine();
					Console.WriteLine("The PKG is ON ! Press Esc to Exit");

					Console.ReadLine();
					if (ps3Tool.Ping())
					{
						onlineStatus = ps3Tool.GetPS3Status();
						Console.WriteLine($"PS3 Online : {onlineStatus.Online}");
						Console.WriteLine($"PS3 Process Runing : {onlineStatus.GameRun}");
						Console.WriteLine($"PS3 /dev_bdvd mounted : {onlineStatus.Mounted}");


						if (!onlineStatus.Online)
						{
							string url = $"http://{argIP}/cpursx.ps3?/sman.ps3";
							Console.WriteLine($"wMAN not answering on {url}");
							return;
						}


						Console.WriteLine();
						if (onlineStatus.Mounted)
						{
							Console.WriteLine("UnMount /dev_bdvd");
							bool isUnMount = ps3Tool.PS3Umount();
							if (isUnMount)
							{
								Console.WriteLine("/dev_bdvd UnMount");
							}
							else
							{
								Console.WriteLine($"Error, /dev_bdvd did not Unmount");
								return;
							}
						}


					}
					ps3Tool.KillPS3Server();


				}
				TaskPS3Serv.Wait();
				ps3Tool.Umount();
				Console.WriteLine("Done");
			}
		}

	}

	static void ShowCommand()
	{
		Console.WriteLine("StreamIsoToPS3.exe <IsoFile> <PS3IP> [--mountpkg]");
	}


	static string HumanReadableFileSize(long sizeInBytes)
	{
		string[] sizes = { "B", "KB", "MB", "GB", "TB" };
		double len = sizeInBytes;
		int order = 0;

		while (len >= 1024 && order < sizes.Length - 1)
		{
			order++;
			len = len / 1024;
		}

		return $"{len:0.#} {sizes[order]}";
	}
}