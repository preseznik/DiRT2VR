Imports System.IO
Imports DiRT2VR
Imports EgoEngineLibrary.Xml

Module StartupMovieTests
    Public Sub Run(repo As String, folder As String, check As Action(Of Boolean, String))
        Dim context As New InstallContext(IO.Path.Combine(folder, "Startup movie game"), IO.Path.Combine(folder, "Startup movie user"))
        Dim selected As New VrSettings With {.SkipStartupMovies = True}
        Files.SaveJson(context.PreferencesPath, selected)
        check(VrSettings.Load(context).SkipStartupMovies, "startup movie skip persists")
        check(Not (New VrSettings()).SkipStartupMovies, "startup movie skipping defaults off")
        Dim rejected As Boolean
        Dim target = IO.Path.Combine(context.GameRoot, "system", "states.bin")
        Directory.CreateDirectory(IO.Path.GetDirectoryName(target))
        File.Copy(IO.Path.Combine(repo, "artifacts/game/system/states.bin"), target)
        Dim transaction As New StartupMovies(context)
        transaction.Prepare()
        Dim applied = Files.Hash(target)
        check(transaction.Pending AndAlso applied <> StartupMovies.OriginalHash, "startup movie changes journaled")
        Using input = File.OpenRead(target)
            Dim document = (New XmlFile(input)).Document
            For Each id In {"sting_video", "intel_sting_video", "amd_sting_video", "ego_sting_video"}
                check(document.SelectNodes("//StateWayPoint[@id='" & id & "'][@timeDelay='0.0']").Count = 1, "startup movie becomes no-delay transition: " & id)
            Next
            check(document.SelectNodes("//StateVideoPreStart[@id='attract_video']").Count = 1 AndAlso document.SelectNodes("//StateVideoPostStart[@id='first_race_video']").Count = 1 AndAlso document.SelectNodes("//StateVideoMemorial[@id='memorial_video']").Count = 1, "startup setting retains attract, first-race and memorial video states")
        End Using
        transaction.Recover()
        check(Not transaction.Pending AndAlso Files.Hash(target) = StartupMovies.OriginalHash, "startup definitions restored byte-for-byte")
        rejected = False
        Try
            transaction.Prepare(Sub() Throw New IOException("interrupted after journal"))
        Catch ex As IOException
            rejected = True
        End Try
        transaction.Recover()
        check(rejected AndAlso Files.Hash(target) = StartupMovies.OriginalHash AndAlso Not transaction.Pending, "interrupted startup preparation recovers")
        transaction.Prepare()
        Dim modified = File.ReadAllBytes(target)
        File.WriteAllText(target, "external edit") : rejected = False
        Try
            transaction.Recover()
        Catch ex As IOException
            rejected = True
        End Try
        check(rejected AndAlso transaction.Pending AndAlso File.ReadAllText(target) = "external edit", "startup recovery preserves outside changes")
        File.WriteAllBytes(target, modified) : transaction.Recover()
        check(Files.Hash(target) = StartupMovies.OriginalHash, "startup conflict can recover after restoring expected modified bytes")
    End Sub
End Module
