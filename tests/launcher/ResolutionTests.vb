Imports DiRT2VR
Imports System.IO
Imports System.IO.MemoryMappedFiles

Public Module ResolutionTests
    Public Sub Run(repo As String, folder As String, check As Action(Of Boolean, String))
        Dim context As New InstallContext(folder, IO.Path.Combine(folder, "resolution-user"))
        Dim path = IO.Path.Combine(context.UserRoot, "resolution.json")
        Dim lastName As String = ""
        For Each scale In {100, 150, 300}
            For Each headsetScale In {50, 100}
                Dim settings As New VrSettings With {.RenderScale = scale, .HeadsetScale = headsetScale}
                Dim start As New ProcessStartInfo()
                start.Environment("DIRT2VR_HEADSET") = "1"
                Using channel As New ResolutionChannel(context, start, settings)
                    Dim name = start.Environment("DIRT2VR_RESOLUTION_CHANNEL")
                    check(name <> lastName, "new resolution session uses a fresh channel") : lastName = name
                    check(Files.ReadJson(Of ResolutionStatus)(path).Actual.All(Function(n) n = 0), "new launch clears stale actual dimensions")
                    For Each source In {New Integer() {1280, 720}, New Integer() {settings.RenderWidth, settings.RenderHeight}}
                        Dim native As New ProcessStartInfo(IO.Path.Combine(repo, "build/ninja/bin/resolution_report_test.exe")) With {.UseShellExecute = False, .CreateNoWindow = True}
                        native.ArgumentList.Add(name)
                        For Each n In source.Concat({3400 * headsetScale \ 100, 3468 * headsetScale \ 100, 3420 * headsetScale \ 100, 3488 * headsetScale \ 100})
                            native.ArgumentList.Add(n.ToString(Globalization.CultureInfo.InvariantCulture))
                        Next
                        Using child = Process.Start(native)
                            If Not child.WaitForExit(10000) Then Throw New Exception("native resolution writer timed out")
                            check(child.ExitCode = 0, "x86 native writer connects to x64 launcher")
                        End Using
                        channel.Poll()
                        Dim report = Files.ReadJson(Of ResolutionStatus)(path)
                        check(report.RequestedWidth = settings.RenderWidth AndAlso report.RequestedHeight = settings.RenderHeight, "requested dimensions survive native reporting")
                        check(report.Actual(0) = source(0) AndAlso report.Actual(4) = 3420 * headsetScale \ 100, "actual source and asymmetric eyes survive reporting")
                        check(report.Description().Contains("differs") = (source(0) <> settings.RenderWidth), "actual mismatch is distinguished from requested resolution")
                    Next
                    Dim previous = File.ReadAllText(path)
                    channel.Poll() : check(File.ReadAllText(path) = previous, "unchanged observations do not rewrite report")
                    Using mapping = MemoryMappedFile.OpenExisting(name), view = mapping.CreateViewAccessor()
                        view.Write(16, 5) : view.Write(20, 999)
                        channel.Poll() : check(File.ReadAllText(path) = previous, "incomplete native writes are ignored")
                        view.Write(20, -1) : view.Write(16, 6)
                        channel.Poll() : check(File.ReadAllText(path) = previous, "invalid dimensions are ignored")
                    End Using
                End Using
            Next
        Next
        Dim bounded As New ProcessStartInfo()
        bounded.Environment("DIRT2VR_HEADSET") = "1"
        Using channel As New ResolutionChannel(context, bounded, New VrSettings()),
            mapping = MemoryMappedFile.OpenExisting(bounded.Environment("DIRT2VR_RESOLUTION_CHANNEL")), view = mapping.CreateViewAccessor()
            For i = 1 To 40
                view.Write(16, i * 2 - 1) : view.Write(20, 1600 + i) : view.Write(24, 1200) : view.Write(16, i * 2)
                channel.Poll()
            Next
            check(Files.ReadJson(Of ResolutionStatus)(path).Actual(0) = 1632, "summary writes are capped during gameplay")
            channel.Poll(True)
            check(Files.ReadJson(Of ResolutionStatus)(path).Actual(0) = 1640, "final snapshot preserves latest size beyond write cap")
        End Using
        Dim desktop As New ProcessStartInfo()
        desktop.Environment("DIRT2VR_RESOLUTION_CHANNEL") = "stale"
        Dim last = File.ReadAllText(path)
        Using channel As New ResolutionChannel(context, desktop, New VrSettings())
            channel.Poll()
            check(Not desktop.Environment.ContainsKey("DIRT2VR_RESOLUTION_CHANNEL") AndAlso File.ReadAllText(path) = last, "desktop strips stale channel and preserves last VR summary")
        End Using
    End Sub
End Module
