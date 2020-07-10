using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Xml;
using System.Xml.XPath;
using LC;
namespace cppkg
{
	public static class Program
	{
		static readonly string _CodeBase = Assembly.GetExecutingAssembly().GetModules()[0].FullyQualifiedName;
		static int Main(string[] args)
		{
#if !DEBUG
			try
			{
#endif
				Run(args, Console.In, Console.Out, Console.Error);
#if !DEBUG
			}
			catch(Exception ex) {
				Console.Error.WriteLine(ex.Message);
				return -1;
			}
#endif
			return 0;
		}
		public static void Run(string[] args,TextReader stdin,TextWriter stdout,TextWriter stderr)
		{
			if (args.Length < 1)
			{
				_PrintUsage(stderr);
				throw new ArgumentException("Not enough arguments specified.");
			}
			var sln = args[0];
			var dir = Path.GetDirectoryName(Path.GetFullPath(sln));
			var zipPath = Path.Combine(dir, Path.GetFileNameWithoutExtension(sln) + ".zip");
			var added = new HashSet<string>();
			if(1!=args.Length && 3!=args.Length)
			{
				_PrintUsage(stderr);
				throw new ArgumentException("One or more arguments is invalid.");
			}
			if (3 == args.Length)
			{
				if ("/output" != args[1].ToLowerInvariant())
				{
					_PrintUsage(stderr);
					throw new ArgumentException("One or more arguments is invalid.");
				}
				zipPath = args[2];
			}
			if (File.Exists(zipPath))
				File.Delete(zipPath);

			using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
			{
				// this will never happen that the sln file has already been added
				// but we keep the check here as a barrier in case we modify code
				// above it, and to make sure it doesn't get readded somehow later
				var slnp = Path.Combine(Path.GetFileNameWithoutExtension(sln), Path.GetFileName(sln));
				if (added.Add(slnp))
				{
					zip.CreateEntryFromFile(sln, slnp);
					stdout.WriteLine(Path.GetFileName(sln));
				}
				else
					stderr.WriteLine(slnp+" (skipped)");
				var ss = _GetSolutionStuff(sln);
				foreach(var fpath in ss.Files)
				{
					var relPath = Path.Combine(Path.GetFileNameWithoutExtension(sln), _GetRelativePath(fpath, dir, true));
					if (added.Add(relPath))
					{
						stderr.Write("\t");
						stdout.WriteLine(relPath);
						zip.CreateEntryFromFile(fpath, relPath);
					}
					else
						stderr.WriteLine("\t" + relPath + " (skipped)");
				}
				stderr.WriteLine();

				foreach (var projPath in ss.Projects)
				{
					var projRelPath = Path.Combine(Path.GetFileNameWithoutExtension(sln), _GetRelativePath(projPath, dir, true));
					if (added.Add(projRelPath))
					{
						stdout.WriteLine(_GetRelativePath(projPath, dir, true));
						zip.CreateEntryFromFile(projPath, projRelPath);
					}
					else
						stderr.WriteLine(_GetRelativePath(projPath, dir, true) + " (skipped)");
					foreach (var filePath in _GetProjectInputs(projPath, new HashSet<string>()))
					{
						var relPath = _GetRelativePath(filePath, dir, true);
						if (added.Add(relPath))
						{
							stderr.Write("\t");
							stdout.WriteLine(relPath);
							zip.CreateEntryFromFile(filePath, Path.Combine(Path.GetFileNameWithoutExtension(sln), relPath));
						}
						else
							stderr.WriteLine("\t" + relPath + " (skipped)");
					}
					stderr.WriteLine();
				}
			}
			stderr.WriteLine();
		}
		static void _PrintUsage(TextWriter w)
		{
			w.Write(Path.GetFileName(_CodeBase));
			w.WriteLine(" <inputFile> [/output <outputFile>]");
			w.WriteLine();
			w.WriteLine("Creates a zip file out of the projects in the solution.");
			w.WriteLine();
			w.WriteLine("\t<inputFile>\tThe input solution file to package");
			w.WriteLine("\t<outputFile>\tThe output zip file to create");
			w.WriteLine();
		}
		static string _GetRelativePath(string path, string relBase, bool throwOnDifferentRoot = true)
		{
			// Copyright (c) 2014, Yves Goergen, http://unclassified.software/source/getrelativepath
			//
			// Copying and distribution of this file, with or without modification, are permitted provided the
			// copyright notice and this notice are preserved. This file is offered as-is, without any warranty.

			// Use case-insensitive comparing of path names.
			// NOTE: This may be different on other systems.
			StringComparison sc = StringComparison.InvariantCultureIgnoreCase;

			// Are both paths rooted?
			if (!Path.IsPathRooted(path))
				throw new ArgumentException("path argument is not a rooted path.");
			if (!Path.IsPathRooted(relBase))
				throw new ArgumentException("relBase argument is not a rooted path.");

			// Do both paths share the same root?
			string pathRoot = Path.GetPathRoot(path);
			string baseRoot = Path.GetPathRoot(relBase);
			if (!string.Equals(pathRoot, baseRoot, sc))
			{
				if (throwOnDifferentRoot)
				{
					throw new InvalidOperationException("Both paths do not share the same root.");
				}
				else
				{
					return path;
				}
			}

			// Cut off the path roots
			path = path.Substring(pathRoot.Length);
			relBase = relBase.Substring(baseRoot.Length);

			// Cut off the common path parts
			string[] pathParts = path.Split(Path.DirectorySeparatorChar);
			string[] baseParts = relBase.Split(Path.DirectorySeparatorChar);
			int commonCount;
			for (
				commonCount = 0;
				commonCount < pathParts.Length &&
				commonCount < baseParts.Length &&
				string.Equals(pathParts[commonCount], baseParts[commonCount], sc);
				commonCount++)
			{
			}

			// Add .. for the way up from relBase
			string newPath = "";
			for (int i = commonCount; i < baseParts.Length; i++)
			{
				newPath += ".." + Path.DirectorySeparatorChar;
			}

			// Append the remaining part of the path
			for (int i = commonCount; i < pathParts.Length; i++)
			{
				newPath = Path.Combine(newPath, pathParts[i]);
			}

			return newPath;
		}

		static (List<string> Projects, List<string> Files) _GetSolutionStuff(string solutionFile)
		{
			var projects = new List<string>();
			var files = new List<string>();
			using (var sr = new StreamReader(solutionFile))
			{
				var dir = Path.GetDirectoryName(solutionFile);
				string line;
				while (null != (line = sr.ReadLine()))
				{
					// i have no idea if the .sln format
					// is case sensitive or not so this 
					// assumes it isn't.
					if (line.ToLowerInvariant().StartsWith("project"))
					{
						var lc = LexContext.Create(line);
						lc.TrySkipLetters();
						lc.TrySkipWhiteSpace();
						if ('(' == lc.Current)
						{
							if(-1!=lc.Advance())
							{
								lc.ClearCapture();
								if(lc.TryReadDosString())
								{
									if ("\"{2150E333-8FDC-42A3-9474-1A3956D46DE8}\"" != lc.GetCapture().ToUpperInvariant())
									{
										lc.TrySkipWhiteSpace();
										if (')' == lc.Current && -1 != lc.Advance())
										{
											lc.TrySkipWhiteSpace();
											if ('=' == lc.Current && -1 != lc.Advance())
											{
												lc.TrySkipWhiteSpace();
												string name;
												lc.ClearCapture();
												if (lc.TryReadDosString())
												{
													name = lc.GetCapture();
													lc.TrySkipWhiteSpace();
													if (',' == lc.Current && -1 != lc.Advance())
													{
														lc.TrySkipWhiteSpace();
														lc.ClearCapture();
														// i'm not actually sure what sort of escapes the string uses.
														string path;
														if (lc.TryReadDosString())
														{
															path = lc.GetCapture();
															path = path.Substring(1, path.Length - 2);
															path = path.Replace("\"\"", "\"");
															path = Path.Combine(dir, path);
															path = Path.GetFullPath(path);
															if(File.Exists(path))
																projects.Add(path);
														}
													}
												}
											}
										}
									}
									else
									{
										lc.TrySkipWhiteSpace();
										if (')' == lc.Current && -1 != lc.Advance())
										{
											while (null != (line = sr.ReadLine().Trim().ToLowerInvariant()) &&
												line != "endproject" &&
												!line.StartsWith("projectsection")) ;
											if(line.StartsWith("projectsection") && 
												null != (line = sr.ReadLine()) &&
												line.Trim().ToLowerInvariant() != "endproject")
											{
												do
												{
													lc = LexContext.Create(line);
													lc.TrySkipWhiteSpace();
													if(lc.TryReadUntil('=',false))
													{
														var fpath = lc.GetCapture().TrimEnd();
														fpath = Path.Combine(dir, fpath);
														fpath = Path.GetFullPath(fpath);
														if(File.Exists(fpath))
															files.Add(fpath);
													}
												} while (null != (line = sr.ReadLine()) &&
												line.Trim().ToLowerInvariant() != "endproject" &&
												!line.Trim().ToLowerInvariant().StartsWith("endprojectsection"));
											}
										}
									}
								}
							}
						}
					}
				}
			}
			return (projects,files);
		}
		static List<string> _GetProjectInputs(string projectFile, HashSet<string> excludedFiles)
		{
			string ns = null;
			var isSdkProj = false;
			using (var r = XmlReader.Create(projectFile))
			{
				while (r.Read() && XmlNodeType.Element != r.NodeType) ;
				if (XmlNodeType.Element != r.NodeType)
					return new List<string>();//throw new IOException("The project file does not contain a valid project.");
				ns = r.NamespaceURI;
				var s = r.GetAttribute("Sdk");
				if (!string.IsNullOrEmpty(s))
					isSdkProj = true;
			}
			var result = new List<string>();
			if (!isSdkProj)
			{
				using (var r = XmlReader.Create(projectFile))
				{
					var d = new XPathDocument(r);
					var nav = d.CreateNavigator();
					var res = new XmlNamespaceManager(nav.NameTable);
					res.AddNamespace("e", ns);
					var iter = nav.Select("/e:Project/e:ItemGroup/*", res);

					while (iter.MoveNext())
					{
						if ("Reference" != iter.Current.LocalName && 
							"ProjectReference" != iter.Current.LocalName &&
							"ProjectConfiguration"!=iter.Current.LocalName)
						{
							var iter2 = iter.Current.Select("@Include");
							if (iter2.MoveNext())
							{
								var file = iter2.Current.Value;
								result.Add(file);
							}
						}
					}
					
				}
				foreach (var e in excludedFiles)
				{
					var s = e;
					if (!Path.IsPathRooted(s))
						s = Path.Combine(Path.GetDirectoryName(projectFile), e);
					try
					{
						// in case it doesn't exist
						s = Path.GetFullPath(s);
					}
					catch { }
					for (int ic = result.Count, i = 0; i < ic; ++i)
					{
						var sc = result[i];
						if (!Path.IsPathRooted(sc))
							sc = Path.Combine(Path.GetDirectoryName(projectFile), sc);
						try
						{
							sc = Path.GetFullPath(sc);
						}
						catch { }
						if (0 == string.Compare(s, sc))
						{
							result.RemoveAt(i);
							--i;
							--ic;
						}
					}
				}
			}
			else // Core or Standard project
			{
				var dir = Path.GetDirectoryName(projectFile);
				var files = Directory.GetFiles(dir, "*.*");
				for (var i = 0; i < files.Length; ++i)
				{
					var f = Path.Combine(dir, files[i]);
					f = Path.GetFullPath(f);
					result.Add(f);
				}
				foreach (string d in Directory.GetDirectories(dir))
				{
					_DirSearch(dir, d, result);
				}
				using (var r = XmlReader.Create(projectFile))
				{
					var d = new XPathDocument(r);
					var nav = d.CreateNavigator();
					var res = new XmlNamespaceManager(nav.NameTable);
					res.AddNamespace("e", ns);
					var iter = nav.Select("/e:Project/e:ItemGroup/*/@Remove", res);
					while (iter.MoveNext())
					{
						var s = iter.Current.Value;
						s = Path.Combine(Path.GetDirectoryName(projectFile), s);
						s = Path.GetFullPath(s);
						result.Remove(s);
					}
					iter = nav.Select("/e:Project/e:ItemGroup/*", res);
					while (iter.MoveNext())
					{
						if ("Reference" != iter.Current.LocalName && 
							"ProjectReference" != iter.Current.LocalName &&
							"ProjectConfiguration"!=iter.Current.LocalName)
						{
							var iter2 = iter.Current.Select("@Include");
							if (iter2.MoveNext())
							{
								var file = iter2.Current.Value;
								result.Add(file);
							}
						}
					}
					
				}
				foreach (var e in excludedFiles)
				{
					var s = e;
					if (!Path.IsPathRooted(s))
						s = Path.Combine(Path.GetDirectoryName(projectFile), e);
					try
					{
						// in case it doesn't exist
						s = Path.GetFullPath(s);
					}
					catch { }
					for (int ic = result.Count, i = 0; i < ic; ++i)
					{
						var sc = result[i];
						if (!Path.IsPathRooted(sc))
							sc = Path.Combine(Path.GetDirectoryName(projectFile), sc);
						try
						{
							sc = Path.GetFullPath(sc);
						}
						catch { }
						if (0 == string.Compare(s, sc))
						{
							result.RemoveAt(i);
							--i;
							--ic;
						}
					}
				}
			}
			if (0 == result.Count)
				return result;
			for (int ic = result.Count, i = 0; i < ic; ++i)
			{
				var s = result[i];
				if (!Path.IsPathRooted(s))
				{
					s = Path.Combine(Path.GetDirectoryName(projectFile), s);
				}
				try
				{
					s = Path.GetFullPath(s);
				}
				catch { }
				if(s==projectFile || !File.Exists(s))
				{
					result.RemoveAt(i);
					--i;
					--ic;
				} else
					result[i] = s;
			}
			return result;
		}
		static void _DirSearch(string root, string currentDir, IList<string> result, bool first = true)
		{

			foreach (string d in Directory.GetDirectories(currentDir))
			{
				foreach (string f in Directory.GetFiles(d, "*.*"))
				{
					result.Add(f);
				}
				var s = currentDir;
				var i = s.LastIndexOfAny(new char[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar });
				if (-1 < i)
					s = s.Substring(i + 1);
				if (!first || (0 != string.Compare(s, "bin") && 0 != string.Compare(s, "obj")))
					_DirSearch(root, d, result, false);
			}
		}
	}
}
