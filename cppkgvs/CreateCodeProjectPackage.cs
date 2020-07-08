using System;
using System.ComponentModel.Design;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Task = System.Threading.Tasks.Task;

namespace cppkgvs
{
	/// <summary>
	/// Command handler
	/// </summary>
	internal sealed class CreateCodeProjectPackage
	{
		/// <summary>
		/// Command ID.
		/// </summary>
		public const int CommandId = 0x0100;

		/// <summary>
		/// Command menu group (command set GUID).
		/// </summary>
		public static readonly Guid CommandSet = new Guid("e727d32a-7705-4641-916d-78702bdbf32c");

		/// <summary>
		/// VS Package that provides this command, not null.
		/// </summary>
		private readonly AsyncPackage package;

		/// <summary>
		/// Initializes a new instance of the <see cref="CreateCodeProjectPackage"/> class.
		/// Adds our command handlers for menu (commands must exist in the command table file)
		/// </summary>
		/// <param name="package">Owner package, not null.</param>
		/// <param name="commandService">Command service to add command to, not null.</param>
		private CreateCodeProjectPackage(AsyncPackage package, OleMenuCommandService commandService)
		{
			this.package = package ?? throw new ArgumentNullException(nameof(package));
			commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));

			var menuCommandID = new CommandID(CommandSet, CommandId);
			var menuItem = new MenuCommand(this.Execute, menuCommandID);
			commandService.AddCommand(menuItem);
		}

		/// <summary>
		/// Gets the instance of the command.
		/// </summary>
		public static CreateCodeProjectPackage Instance {
			get;
			private set;
		}

		/// <summary>
		/// Gets the service provider from the owner package.
		/// </summary>
		private Microsoft.VisualStudio.Shell.IAsyncServiceProvider ServiceProvider {
			get {
				return this.package;
			}
		}

		/// <summary>
		/// Initializes the singleton instance of the command.
		/// </summary>
		/// <param name="package">Owner package, not null.</param>
		public static async Task InitializeAsync(AsyncPackage package)
		{
			// Switch to the main thread - the call to AddCommand in CreateCodeProjectPackage's constructor requires
			// the UI thread.
			await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

			OleMenuCommandService commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
			Instance = new CreateCodeProjectPackage(package, commandService);
		}
		
		/// <summary>
		/// This function is the callback used to execute the command when the menu item is clicked.
		/// See the constructor to see how the menu item is associated with this function using
		/// OleMenuCommandService service and MenuCommand class.
		/// </summary>
		/// <param name="sender">Event sender.</param>
		/// <param name="e">Event args.</param>
		private void Execute(object sender, EventArgs e)
		{
			ThreadHelper.ThrowIfNotOnUIThread();
			var task = package.GetServiceAsync(typeof(DTE));
			var dte = task.ConfigureAwait(true).GetAwaiter().GetResult() as DTE2;
			ThreadHelper.ThrowIfNotOnUIThread();
			var sln = dte.Solution.FullName;
			var zip = Path.Combine(Path.GetDirectoryName(sln), Path.GetFileNameWithoutExtension(sln) + ".zip");
			cppkg.Program.Run(new string[] { sln, "/output",zip}, TextReader.Null, TextWriter.Null, TextWriter.Null);
			_SelectShellFile(zip);
		}
		private void _SelectShellFile(string path)
		{
			if (string.IsNullOrEmpty(path))
				throw new ArgumentNullException("path");

			path = Path.GetFullPath(path);

			IntPtr pids = ILCreateFromPathW(path);
			if (IntPtr.Zero!=pids)
				try
				{
					Marshal.ThrowExceptionForHR(SHOpenFolderAndSelectItems(pids, 0, IntPtr.Zero, 0));
				}
				finally
				{
					ILFree(pids);
				}
		}

		[DllImport("shell32.dll", ExactSpelling = true)]
		public static extern void ILFree(IntPtr pidlList);

		[DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
		public static extern IntPtr ILCreateFromPathW(string pszPath);

		[DllImport("shell32.dll", ExactSpelling = true)]
		public static extern int SHOpenFolderAndSelectItems(IntPtr pidlList, uint cild, IntPtr children, uint dwFlags);
		
	}
}
