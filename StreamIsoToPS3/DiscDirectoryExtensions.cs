using DiscUtils;

namespace StreamIsoToPS3
{
	public static class DiscDirectoryExtensions
	{
		public static IEnumerable<DiscFileInfo> GetAllFiles(this DiscDirectoryInfo directory)
		{
			foreach (var file in directory.GetFiles())
			{
				yield return file;
			}

			foreach (var subDirectory in directory.GetDirectories())
			{
				foreach (var file in GetAllFiles(subDirectory))
				{
					yield return file;
				}
			}
		}
	}
}
