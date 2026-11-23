' NVIDIA's driver maps a game's whole DirectX shader cache file into the game. DiRT 2 is a 32-bit game, so once
' its cache file has grown to 512 MB the sound engine's start-up memory no longer fits and the game closes while
' loading, without an error. The driver rebuilds the cache, so an oversized DiRT 2 cache file is removed before
' launch. Only files whose header names dirt2_game.exe are touched; other programs' caches are left alone.
Public NotInheritable Class ShaderCache
    Public Const LimitBytes As Long = 512L * 1024 * 1024
    Private Shared ReadOnly Magic As Byte() = Text.Encoding.ASCII.GetBytes("nvph")
    Private Shared ReadOnly GameName As Byte() = Text.Encoding.ASCII.GetBytes("dirt2_game.exe" & ChrW(0))
    Private Const NameOffset As Integer = 13

    Public Shared Function Folders() As String()
        Dim local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
        Return {IO.Path.Combine(local, "NVIDIA", "DXCache"), IO.Path.Combine(IO.Path.GetDirectoryName(local), "LocalLow", "NVIDIA", "DXCache")}
    End Function

    ' The DiRT 2 cache files at or above the limit. Unreadable files are skipped.
    Public Shared Function Oversized(folders As IEnumerable(Of String), Optional limit As Long = LimitBytes) As List(Of String)
        Dim result As New List(Of String)
        For Each folder In folders
            If Not Directory.Exists(folder) Then Continue For
            For Each file In Directory.EnumerateFiles(folder, "*.nvph")
                Try
                    If New FileInfo(file).Length >= limit AndAlso IsDirt2(file) Then result.Add(file)
                Catch ex As IOException
                Catch ex As UnauthorizedAccessException
                End Try
            Next
        Next
        Return result
    End Function

    ' "nvph", then the owning program's file name, NUL-terminated, at byte 13 (byte 12 varies between files).
    Public Shared Function IsDirt2(file As String) As Boolean
        Dim header(63) As Byte, count As Integer
        Using stream As New FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite Or FileShare.Delete)
            count = stream.ReadAtLeast(header, header.Length, throwOnEndOfStream:=False)
        End Using
        Return StartsAt(header, count, Magic, 0) AndAlso StartsAt(header, count, GameName, NameOffset)
    End Function
    Private Shared Function StartsAt(data As Byte(), count As Integer, value As Byte(), index As Integer) As Boolean
        If index + value.Length > count Then Return False
        For i = 0 To value.Length - 1
            If data(index + i) <> value(i) Then Return False
        Next
        Return True
    End Function

    ' Removes the oversized DiRT 2 cache files and returns how many. A file that cannot be removed stops the launch
    ' with its location, since the game would close while loading anyway.
    Public Shared Function Clear(folders As IEnumerable(Of String), Optional limit As Long = LimitBytes) As Integer
        Dim files = Oversized(folders, limit)
        For Each file In files
            Try
                IO.File.Delete(file)
            Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
                Throw New IOException("NVIDIA's shader cache for DiRT 2 has grown too large, which closes the game while it loads, and DiRT2VR could not remove it. Close DiRT 2 and delete " & file & ", or clear the shader cache with the NVIDIA app.", ex)
            End Try
        Next
        Return files.Count
    End Function
End Class
