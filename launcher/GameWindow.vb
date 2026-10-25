Public Module GameWindow
    Private Delegate Function EnumWindowCallback(window As IntPtr, data As IntPtr) As Boolean
    <DllImport("user32.dll")> Private Function EnumWindows(callback As EnumWindowCallback, data As IntPtr) As Boolean
    End Function
    <DllImport("user32.dll")> Private Function GetWindowThreadProcessId(window As IntPtr, ByRef processId As UInteger) As UInteger
    End Function
    <DllImport("user32.dll")> Private Function IsWindowVisible(window As IntPtr) As Boolean
    End Function
    <DllImport("user32.dll", CharSet:=CharSet.Unicode)> Private Function GetClassName(window As IntPtr, name As Text.StringBuilder, length As Integer) As Integer
    End Function
    Public Function Find(executable As String, Optional rendererOnly As Boolean = True) As IntPtr
        Dim result As IntPtr
        For Each game In Process.GetProcessesByName(IO.Path.GetFileNameWithoutExtension(executable))
            Using game
                Try
                    If String.Equals(game.MainModule.FileName, executable, StringComparison.OrdinalIgnoreCase) Then
                        result = If(rendererOnly, FindRenderer(CUInt(game.Id)), game.MainWindowHandle)
                        If result <> IntPtr.Zero Then Return result
                    End If
                Catch ex As Exception When TypeOf ex Is ComponentModel.Win32Exception OrElse TypeOf ex Is InvalidOperationException
                    ' Process can exit during startup or recovery.
                End Try
            End Using
        Next
        Return result
    End Function
    Private Function FindRenderer(processId As UInteger) As IntPtr
        Dim result As IntPtr
        EnumWindows(Function(window, data)
                        Dim owner As UInteger
                        GetWindowThreadProcessId(window, owner)
                        If owner = processId AndAlso IsWindowVisible(window) Then
                            Dim name As New Text.StringBuilder(256)
                            GetClassName(window, name, name.Capacity)
                            ' InitialLoading and offline-provider windows are not the renderer.
                            If name.ToString().StartsWith("NeonClass_", StringComparison.Ordinal) Then
                                result = window : Return False
                            End If
                        End If
                        Return True
                    End Function, IntPtr.Zero)
        Return result
    End Function
End Module
