using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace BizHawk.Common
{
	/// <summary>
	/// Starts a thread which cleans any filenames in %temp% beginning with bizhawk.bizdelete.
	/// Files shouldn't be named that unless they're safe to delete, but notably, they may stil be in use. That won't hurt this component.
	/// When they're no longer in use, this component will then be able to delete them.
	/// </summary>
	public static class TempFileManager
	{
		// TODO - manage paths other than %temp%, make not static, or allow adding multiple paths to static instance

		public static string GetTempFilename(string friendlyname, string extension = null, bool delete = true)
		{
			string guidPart = Guid.NewGuid().ToString();
			var fname = $"biz-{System.Diagnostics.Process.GetCurrentProcess().Id}-{friendlyname}-{guidPart}{extension ?? ""}";
			if (delete)
			{
				fname = RenameTempFilenameForDelete(fname);
			}

			return Path.Combine(Path.GetTempPath(), fname);
		}

		public static string RenameTempFilenameForDelete(string path)
		{
			string filename = Path.GetFileName(path);
			string dir = Path.GetDirectoryName(path);
			if (!filename.StartsWith("biz-"))
			{
				throw new InvalidOperationException();
			}

			filename = "bizdelete-" + filename.Remove(0, 4);
			return Path.Combine(dir, filename);
		}

		public static void Start()
		{
			lock (typeof(TempFileManager))
			{
				if (thread != null)
				{
					return;
				}

				thread = new Thread(ThreadProc)
				{
					IsBackground = true,
					Priority = ThreadPriority.Lowest
				};
				thread.Start();
			}
		}

		#if WINDOWS
		[DllImport("kernel32.dll", EntryPoint = "DeleteFileW", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
		static extern bool DeleteFileW([MarshalAs(UnmanagedType.LPWStr)]string lpFileName);
		#endif

		static void ThreadProc()
		{
			//squirrely logic, trying not to create garbage
			HashSet<string> knownTempDirs = new HashSet<string>();
			List<DirectoryInfo> dis = new List<DirectoryInfo>();
			for (;;)
			{
				lock (typeof(TempFileManager))
				{
					knownTempDirs.Add(Path.GetTempPath());
					if (dis.Count != knownTempDirs.Count)
						dis = knownTempDirs.Select(x => new DirectoryInfo(x)).ToList();
				}

				foreach(var di in dis)
				{
					FileInfo[] fis = null;
					try
					{
						fis = di.GetFiles("bizdelete-*");
					}
					catch
					{
					}
					if(fis != null)
					{
						foreach (var fi in fis)
						{
							try
							{
								// SHUT. UP. THE. EXCEPTIONS.
								#if WINDOWS
								DeleteFileW(fi.FullName);
								#else
								fi.Delete();
								#endif
							}
							catch
							{
							}

							// try not to do more than one thing per frame
							Thread.Sleep(100);
						}
					}
				}

				// try not to slam the filesystem too hard, we dont want this to cause any hiccups
				Thread.Sleep(5000);
			}
		}

		public static void Stop()
		{
		}

		static Thread thread;

		public static void HelperSetTempPath(string path)
		{
			//yes... this is how we're doing it, for now, until it's proven to be troublesome
			Directory.CreateDirectory(path);
			Environment.SetEnvironmentVariable("TMP", path);
			Environment.SetEnvironmentVariable("TEMP", path);
		}
	}
}