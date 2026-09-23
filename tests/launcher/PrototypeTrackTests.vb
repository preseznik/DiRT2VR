Imports System.IO
Imports DiRT2VR

Public Module PrototypeTrackTests
    Public Sub Run(folder As String, check As Action(Of Boolean, String))
        Dim context As New InstallContext(Path.Combine(folder, "prototype-fixture"), Path.Combine(folder, "prototype-owner"))
        Dim track = RaceCatalog.Current.Track(PrototypeTrack.Id)
        Dim route = track.Folder(context)
        Dim reject As Action(Of Action, String) = Sub(action, name)
                                                     Dim failed As Boolean
                                                     Try
                                                         action()
                                                     Catch ex As IOException
                                                         failed = True
                                                     End Try
                                                     check(failed, name)
                                                 End Sub
        check(Not PrototypeTrack.Installed(track, context), "prototype hidden when absent")
        Directory.CreateDirectory(route)
        check(Not PrototypeTrack.Installed(track, context), "unmarked donor clone stays hidden")
        Dim receipt As New PrototypeTrack.Receipt With {.Schema = 1, .TrackId = PrototypeTrack.Id}
        For Each name In PrototypeTrack.RequiredFiles
            File.WriteAllText(Path.Combine(route, name), "fixture: " & name)
            receipt.Files(name) = Files.Hash(Path.Combine(route, name))
        Next
        Dim marker = Path.Combine(route, "prototype.json")
        Files.SaveJson(marker, receipt)
        PrototypeTrack.Validate(route)
        check(PrototypeTrack.Installed(track, context), "complete prototype is selectable")
        For Each name In {"track.jpk", "track.vis", "objects.ens", "ornaments.xml", "ornaments.bin"}
            File.AppendAllText(Path.Combine(route, name), "changed")
            reject(Sub() PrototypeTrack.Validate(route), "changed prototype asset is rejected: " & name)
            File.WriteAllText(Path.Combine(route, name), "fixture: " & name)
        Next
        receipt.Schema = 2 : Files.SaveJson(marker, receipt)
        reject(Sub() PrototypeTrack.Validate(route), "unknown receipt schema rejected")
        receipt.Schema = 1 : Files.SaveJson(marker, receipt)
        PrototypeTrack.ValidateMode(track.Id, "practice", False)
        check(True, "desktop solo allowed")
        reject(Sub() PrototypeTrack.ValidateMode(track.Id, "practice", True), "prototype VR rejected")
        reject(Sub() PrototypeTrack.ValidateMode(track.Id, "race", False), "prototype Race rejected")
        reject(Sub() RaceCatalog.Current.Config(track.Id, "sti", 1), "prototype AI grid rejected")
        Dim document = XmlPatches.Read(RaceCatalog.Current.Config(track.Id, "sti"))
        check(document.SelectSingleNode("/config/track/@name").Value = "d2vr_test" AndAlso document.SelectSingleNode("/config/track/@route").Value = "route_0", "new identity written to direct config")
        Dim camera = Path.Combine(context.GameRoot, "cars/sti/cameras.xml")
        Directory.CreateDirectory(Path.GetDirectoryName(camera)) : File.WriteAllText(camera, "unchanged camera")
        Dim transaction As New AssetTransaction(context)
        reject(Sub() transaction.Prepare(trackId:=track.Id), "VR file worker refuses prototype")
        check(Not transaction.Pending, "rejection creates no recovery journal")
        For attempt = 1 To 2
            transaction.Prepare(trackId:=track.Id, configOnly:=True)
            check(transaction.Pending, "desktop preparation creates recoverable config")
            transaction.Recover()
            check(Not transaction.Pending AndAlso Not File.Exists(Path.Combine(context.ModRoot, "p.xml")), "desktop prototype recovery completes")
        Next
        check(File.ReadAllText(camera) = "unchanged camera", "desktop prototype preserves camera")
        PrototypeTrack.Validate(route)
        check(True, "session recovery preserves installed track files")
        check(RaceCatalog.Current.Tracks.Where(Function(t) t.Id <> PrototypeTrack.Id).Count() = 41, "all 41 original routes retained")
    End Sub
End Module
