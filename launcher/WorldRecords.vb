Imports System.Data
Imports DiRT2VR.CustomTracks
Imports EgoEngineLibrary.Data

' The game's world_record table: one time in seconds per track model and vehicle
' class. Read from the database the session will use: the game's own for stock
' routes, or the layout's session database for custom tracks.
Public Module WorldRecords
    Private ReadOnly cache As New Dictionary(Of String, (Length As Long, Written As DateTime, Records As Dictionary(Of String, Single)))(StringComparer.OrdinalIgnoreCase)
    Public Function DatabasePath(context As InstallContext, trackId As String, Optional receipt As PackReceipt = Nothing) As String
        If Not TrackPacks.IsLayout(trackId) Then Return IO.Path.Combine(context.GameRoot, "database", "database.bin")
        Dim pack = TrackPacks.ForLayout(trackId)
        If pack.External IsNot Nothing Then
            Dim replacement = pack.External.Layout(trackId).Session.SingleOrDefault(Function(s) s.Target = "database/database.bin")
            If replacement Is Nothing Then Throw New IOException("External layout has no session database.")
            Return SafeFiles.Inside(pack.External.Root, replacement.File)
        End If
        receipt = If(receipt, pack.Read(context.GameRoot, False))
        Dim session = receipt.Sessions.FirstOrDefault(Function(s) s.LayoutId = trackId)?.Files.FirstOrDefault(Function(f) f.Path = "database/database.bin")
        If session Is Nothing Then Throw New IOException("Custom layout has no session database.")
        Return SafeFiles.Inside(context.GameRoot, session.InstalledPath)
    End Function
    ' Keys are "folder/file/route|class"; "folder/file/route|" marks a track with any record.
    Public Function Read(path As String) As Dictionary(Of String, Single)
        Files.NoLinks(path)
        Dim info As New FileInfo(path)
        If Not info.Exists Then Throw New IOException("The session database is missing.")
        Dim cached As (Length As Long, Written As DateTime, Records As Dictionary(Of String, Single)) = Nothing
        If cache.TryGetValue(path, cached) AndAlso cached.Length = info.Length AndAlso cached.Written = info.LastWriteTimeUtc Then
            If cached.Records Is Nothing Then Throw New IOException("The session database could not be read.")
            Return cached.Records
        End If
        ' A failed read is remembered until the file changes; the launcher refreshes every second.
        cache(path) = (info.Length, info.LastWriteTimeUtc, Nothing)
        ' EGO's database reader requires a schema filename. Keep it private.
        Dim folder = IO.Path.Combine(IO.Path.GetTempPath(), "DiRT2VR-records-" & Guid.NewGuid().ToString("N"))
        Files.NoLinks(folder) : Directory.CreateDirectory(folder)
        Dim schema = IO.Path.Combine(folder, "schema.xml")
        Try
            Using resource = GetType(RaceCatalog).Assembly.GetManifestResourceStream("DiRT2VR.schemaDirt2.xml"), target = File.Create(schema)
                resource.CopyTo(target)
            End Using
            Dim database As New DatabaseFile(path, schema)
            Dim models As New Dictionary(Of Integer, String), classes As New Dictionary(Of Integer, String)
            For Each row In database.Tables("track_model").Rows.Cast(Of DataRow)()
                models(CInt(row("id"))) = CStr(row("folder_string")) & "/" & CStr(row("file_string")) & "/" & CStr(row("route_string"))
            Next
            For Each row In database.Tables("vehicle_class").Rows.Cast(Of DataRow)()
                classes(CInt(row("id"))) = CStr(row("file_string"))
            Next
            Dim records As New Dictionary(Of String, Single)(StringComparer.OrdinalIgnoreCase)
            For Each row In database.Tables("world_record").Rows.Cast(Of DataRow)()
                Dim model = "", vehicleClass = "", seconds = CSng(row("world_record")), current As Single
                If Not models.TryGetValue(CInt(row("track_model_id")), model) OrElse Not classes.TryGetValue(CInt(row("vehicle_class_id")), vehicleClass) OrElse
                    Not Single.IsFinite(seconds) OrElse seconds < 1 OrElse seconds > 7200 Then Continue For
                records(model & "|") = 0
                If Not records.TryGetValue(model & "|" & vehicleClass, current) OrElse seconds < current Then records(model & "|" & vehicleClass) = seconds
            Next
            cache(path) = (info.Length, info.LastWriteTimeUtc, records)
            Return records
        Catch ex As Exception When TypeOf ex Is ArgumentException OrElse TypeOf ex Is InvalidCastException OrElse TypeOf ex Is InvalidOperationException OrElse
            TypeOf ex Is IndexOutOfRangeException OrElse TypeOf ex Is NullReferenceException OrElse TypeOf ex Is Xml.XmlException
            Throw New IOException("The session database could not be read.", ex)
        Finally
            If File.Exists(schema) Then File.Delete(schema)
            Directory.Delete(folder)
        End Try
    End Function
    ' Empty when the database cannot be read, so the launcher shows no misleading record.
    Public Function Describe(context As InstallContext, trackId As String, carCode As String, Optional receipt As PackReceipt = Nothing) As String
        Try
            Dim track = RaceCatalog.Current.Track(trackId), vehicle = RaceCatalog.Current.Car(carCode)
            ' External cars register under their donor's class.
            Dim className = If(RaceCatalog.Current.Cars.FirstOrDefault(Function(c) c.ClassId = vehicle.ClassId)?.ClassName, vehicle.ClassName)
            Dim records = Read(DatabasePath(context, trackId, receipt))
            Dim model = track.Country & "/" & track.Track & "/" & track.Route, seconds As Single
            If records.TryGetValue(model & "|" & vehicle.ClassId, seconds) Then Return "World record · " & className & ": " & BestLapStore.Time(CLng(Math.Round(seconds * 1000)))
            Return If(records.ContainsKey(model & "|"), "World record: none for the " & className & " class", "World record: none for this track")
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
            Return ""
        End Try
    End Function
End Module
