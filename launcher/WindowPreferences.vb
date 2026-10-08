Imports System.Drawing
Imports System.Text.Json
Imports System.Windows.Forms

' Separate from game settings so closing the window never saves uncommitted edits.
Public Class WindowPreferences
    Public Property Version As Integer = 1
    Public Property Width As Integer
    Public Property Height As Integer
    Public Property Maximized As Boolean

    Public Shared Function Load(context As InstallContext) As WindowPreferences
        Try
            Dim path = IO.Path.Combine(context.UserRoot, "window-preferences.json")
            If File.Exists(path) Then
                Dim value = Files.ReadJson(Of WindowPreferences)(path)
                If value IsNot Nothing AndAlso value.Version = 1 AndAlso value.Width >= 100 AndAlso value.Width <= 10000 AndAlso value.Height >= 100 AndAlso value.Height <= 10000 Then Return value
            End If
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is JsonException OrElse TypeOf ex Is UnauthorizedAccessException
            ' A damaged or inaccessible window preference must not prevent startup.
        End Try
        Return New WindowPreferences()
    End Function

    Public Function Fit(workArea As Size, dpi As Integer, fallback As Size) As Size
        Dim scale = Math.Clamp(dpi, 96, 768) / 96.0
        Dim w = If(Width >= 100 AndAlso Width <= 10000, CInt(Width * scale), fallback.Width)
        Dim h = If(Height >= 100 AndAlso Height <= 10000, CInt(Height * scale), fallback.Height)
        Return New Size(Math.Clamp(w, Math.Min(CInt(560 * scale), workArea.Width), workArea.Width),
                        Math.Clamp(h, Math.Min(CInt(540 * scale), workArea.Height), workArea.Height))
    End Function

    Public Shared Function Capture(normalSize As Size, dpi As Integer, maximized As Boolean) As WindowPreferences
        Dim scale = Math.Clamp(dpi, 96, 768) / 96.0
        Return New WindowPreferences With {.Width = CInt(normalSize.Width / scale), .Height = CInt(normalSize.Height / scale), .Maximized = maximized}
    End Function

    Public Sub Save(context As InstallContext)
        If Width < 100 OrElse Height < 100 Then Return
        Try
            Dim path = IO.Path.Combine(context.UserRoot, "window-preferences.json")
            Files.NoLinks(path)
            Files.SaveJson(path, Me)
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
            ' Window persistence is optional; never block exit or game recovery.
        End Try
    End Sub
End Class
