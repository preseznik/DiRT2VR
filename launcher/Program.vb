Imports System.Windows.Forms
Imports System.Security.Principal

Public Module Program
    <STAThread>
    Public Function Main(args As String()) As Integer
        Try
            ' Theme initialization can create a hidden HWND. Set text rendering first,
            ' including in background session/worker processes launched by the UI.
            Application.SetCompatibleTextRenderingDefault(False)
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2)
            Application.SetColorMode(SystemColorMode.System)
            Application.EnableVisualStyles()
            Dim root = Argument(args, "--game", AppContext.BaseDirectory)
            Dim context As New InstallContext(root, If(args.Contains("--worker"), Argument(args, "--owner-base", Nothing), Nothing))
            If args.Contains("--worker") Then
                Try
                    Worker.Run(context, Argument(args, "--worker", ""), Argument(args, "--car", "sti"), Argument(args, "--track", Nothing), Integer.Parse(Argument(args, "--opponents", "0"), Globalization.CultureInfo.InvariantCulture), Argument(args, "--opponent-cars", "same"))
                    Return 0
                Catch ex As UnauthorizedAccessException
                    Return 5
                End Try
            End If
            If args.Contains("--check-install") Then
                context.ValidateGame() : context.RequireClosed()
                Dim target = IO.Path.Combine(context.GameRoot, "d3d11.dll")
                If File.Exists(target) Then
                    Dim receipt = IO.Path.Combine(context.ModRoot, "installation.json")
                    If Not File.Exists(receipt) OrElse Files.Hash(target) <> Files.ReadJson(Of InstallationReceipt)(receipt).ProxyHash Then Throw New IOException("An unrecognized d3d11.dll exists. Setup will not overwrite another mod.")
                End If
                Return 0
            End If
            If args.Contains("--discover") Then
                ' Used by the installer before selecting its destination.
                Dim output = Argument(args, "--discover", "")
                Files.AtomicWrite(output, Text.Encoding.UTF8.GetBytes(Discovery.GamePath()))
                Return 0
            End If
            If args.Contains("--setup") OrElse args.Contains("--recover") OrElse args.Contains("--remove-proxy") Then
                Using guard As New Mutex(False, "Global\DiRT2VR.Session")
                    Dim held As Boolean
                    Try
                        held = guard.WaitOne(0)
                    Catch ex As AbandonedMutexException
                        held = True
                    End Try
                    If Not held Then Throw New IOException("A DiRT2VR session is running.")
                    Try
                        context.RequireClosed()
                        Call (New GraphicsTransaction(context)).Recover()
                        Worker.Invoke(context, "recover")
                        If args.Contains("--setup") Then Worker.Invoke(context, "setup")
                        If args.Contains("--remove-proxy") Then Worker.Invoke(context, "remove")
                    Finally
                        guard.ReleaseMutex()
                    End Try
                End Using
                Return 0
            End If
            If args.Contains("--launch") Then
                If New WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator) Then Throw New IOException("Start DiRT2VR normally, without Run as administrator. Only the file worker needs elevation.")
                Call (New Session(context, args.Contains("--lan-host"), Argument(args, "--lan-join", Nothing))).Run(Not args.Contains("--desktop"), args.Contains("--vr"))
            Else
                Application.Run(New MainForm(context))
            End If
            Return 0
        Catch ex As Exception
            If args.Contains("--quiet") Then
                Console.Error.WriteLine(ex.Message)
            Else
                MessageBox.Show(ex.Message, "DiRT2VR", MessageBoxButtons.OK, MessageBoxIcon.Error)
            End If
            Return 1
        End Try
    End Function
    Private Function Argument(args As String(), name As String, fallback As String) As String
        Dim index = Array.IndexOf(args, name)
        If index < 0 Then Return fallback
        If index + 1 >= args.Length Then Throw New ArgumentException("Missing value for " & name)
        Return args(index + 1)
    End Function
End Module
