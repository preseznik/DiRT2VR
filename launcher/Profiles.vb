Imports DiRT2VR.Profiles

' Career selection is separate from graphics and controller preferences.
Public Class ProfileService
    Private ReadOnly context As InstallContext
    Public ReadOnly Store As ProfileStore
    Public ReadOnly SelectionPath As String
    Public Sub New(value As InstallContext)
        context = value
        Store = New ProfileStore(IO.Path.Combine(IO.Path.GetDirectoryName(context.UserRoot), "profiles"))
        SelectionPath = IO.Path.Combine(context.UserRoot, "profile-selection.json")
    End Sub
    Public Function SelectedId() As String
        Return Store.LoadSelection(SelectionPath)
    End Function
    Public Sub SelectProfile(id As String)
        Store.Select(SelectionPath, id, AddressOf context.RequireClosed)
    End Sub
    Public Sub DeleteProfile(id As String)
        Store.Delete(SelectionPath, id, AddressOf context.RequireClosed)
    End Sub
    Public Function Create(name As String, completed As Boolean) As ProfileInfo
        Return Store.Create(name, completed, AddressOf context.RequireClosed, selectionPath:=SelectionPath)
    End Function
End Class

' Captured once while the session guard is held, including return-to-menus.
Public Class ProfileSession
    Private ReadOnly context As InstallContext
    Private ReadOnly service As ProfileService
    Private ReadOnly selected As String
    Public ReadOnly Managed As Boolean
    Public Sub New(value As InstallContext)
        context = value : service = New ProfileService(context)
        selected = service.SelectedId()
        Managed = selected <> ProfileStore.CurrentCareer
    End Sub
    Public Sub Prepare(multiplayer As Boolean)
        If Managed Then service.Store.Read(selected)
        If multiplayer OrElse Managed Then Worker.Invoke(context, If(multiplayer, "prepare-lan", "prepare-profile"))
    End Sub
    Public Sub Configure(start As ProcessStartInfo, multiplayer As Boolean)
        If Not Managed Then Return
        ' Refuse mixed/older packages before the game can fall back to its normal save.
        ' The worker separately verifies the DLL against the package hash.
        Dim payload = IO.Path.Combine(context.ModRoot, "payload", "xlive-lan.dll")
        Files.NoLinks(payload)
        If Not File.Exists(payload) OrElse New FileInfo(payload).Length > 16 * 1024 * 1024 OrElse
            Not Text.Encoding.Latin1.GetString(File.ReadAllBytes(payload)).Contains("Managed career mount active", StringComparison.Ordinal) Then
            Throw New IOException("This package does not support separate careers. Reinstall the complete current package before launching this profile.")
        End If
        service.Store.Read(selected)
        If Not multiplayer Then LanSession.ConfigureProvider(context, start, False)
        start.Environment("DIRT2VR_PROFILE_ROOT") = service.Store.ProfileRoot(selected)
    End Sub
    Public Sub ConfirmStartup(multiplayer As Boolean)
        If Not multiplayer AndAlso Not Managed Then Return
        Dim receipt = LanSession.ReceiptPath(context)
        If Not File.Exists(receipt) Then Throw New IOException("The game exited before offline profile startup was confirmed.")
        If Managed AndAlso Not File.ReadAllText(receipt).Contains("Managed career mount active", StringComparison.Ordinal) Then
            Throw New IOException("The offline provider did not confirm the selected career. Reinstall the complete current package.")
        End If
    End Sub
End Class
