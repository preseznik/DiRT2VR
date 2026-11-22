Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms

' Photos saved in Photo Mode (Pictures\DiRT2VR). VR photos are side by side, left eye on
' the left, and their names end in -SBS.png.
Public NotInheritable Class PhotoLibrary
    Public Const StereoSuffix As String = "-SBS.png"
    Public Shared ReadOnly Property Folder As String
        Get
            Return IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "DiRT2VR")
        End Get
    End Property
    Public Shared Function IsStereo(path As String) As Boolean
        Return path.EndsWith(StereoSuffix, StringComparison.OrdinalIgnoreCase)
    End Function
    ' Newest first; at most a few hundred, so the list stays quick.
    Public Shared Function Items(Optional limit As Integer = 400) As List(Of IO.FileInfo)
        If Not Directory.Exists(Folder) Then Return New List(Of IO.FileInfo)
        Return New DirectoryInfo(Folder).EnumerateFiles("*.png", SearchOption.TopDirectoryOnly).
            Where(Function(f) (f.Attributes And FileAttributes.ReparsePoint) = 0).
            OrderByDescending(Function(f) f.LastWriteTimeUtc).Take(limit).ToList()
    End Function
    ' Loads without keeping the file open, so photos can be deleted or renamed while shown.
    Public Shared Function Load(path As String) As Bitmap
        Using stream As New MemoryStream(File.ReadAllBytes(path)), decoded = Image.FromStream(stream)
            Return New Bitmap(decoded)
        End Using
    End Function
    ' One eye of a side-by-side photo, both halves swapped (cross-eyed viewing), or the photo as saved.
    Public Shared Function View(photo As Bitmap, mode As StereoView) As Bitmap
        Dim half = photo.Width \ 2
        Select Case mode
            Case StereoView.LeftEye : Return photo.Clone(New Rectangle(0, 0, half, photo.Height), photo.PixelFormat)
            Case StereoView.RightEye : Return photo.Clone(New Rectangle(half, 0, photo.Width - half, photo.Height), photo.PixelFormat)
            Case StereoView.CrossEyed
                Dim swapped As New Bitmap(photo.Width, photo.Height)
                Using g = Graphics.FromImage(swapped)
                    g.DrawImage(photo, New Rectangle(0, 0, photo.Width - half, photo.Height), New Rectangle(half, 0, photo.Width - half, photo.Height), GraphicsUnit.Pixel)
                    g.DrawImage(photo, New Rectangle(photo.Width - half, 0, half, photo.Height), New Rectangle(0, 0, half, photo.Height), GraphicsUnit.Pixel)
                End Using
                Return swapped
            Case Else : Return New Bitmap(photo)
        End Select
    End Function
    Public Shared Function Thumbnail(path As String, size As Size) As Bitmap
        Using photo = Load(path)
            ' A stereo photo shows its left eye, so the thumbnail is not two small pictures.
            Using source = If(IsStereo(path), View(photo, StereoView.LeftEye), New Bitmap(photo))
                Dim result As New Bitmap(size.Width, size.Height)
                Using g = Graphics.FromImage(result)
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic : g.Clear(Color.Black)
                    Dim scale = Math.Min(size.Width / source.Width, size.Height / source.Height)
                    Dim w = CInt(source.Width * scale), h = CInt(source.Height * scale)
                    g.DrawImage(source, (size.Width - w) \ 2, (size.Height - h) \ 2, w, h)
                    If IsStereo(path) Then
                        Using badge As New SolidBrush(Color.FromArgb(200, 0, 0, 0)), font As New Font("Segoe UI", 8, FontStyle.Bold)
                            g.FillRectangle(badge, 4, 4, 34, 16)
                            TextRenderer.DrawText(g, "SBS", font, New Rectangle(4, 4, 34, 16), Color.FromArgb(183, 232, 49), TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
                        End Using
                    End If
                End Using
                Return result
            End Using
        End Using
    End Function
    Public Shared Sub Recycle(path As String)
        Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin)
    End Sub
End Class

Public Enum StereoView
    SideBySide
    LeftEye
    RightEye
    CrossEyed
End Enum

' Launcher → Photos: the saved photos, with a viewer and simple management.
Public Class PhotoPanel
    Inherits VerticalStack
    Private Shared ReadOnly ThumbSize As New Size(192, 108)
    Private ReadOnly grid As New ListView With {.Name = "SavedPhotos", .AccessibleName = "Saved photos", .View = View.LargeIcon, .Height = 380,
        .MultiSelect = True, .HideSelection = False}
    Private ReadOnly images As New ImageList With {.ColorDepth = ColorDepth.Depth32Bit, .ImageSize = ThumbSize}
    Private ReadOnly detail As New Label With {.AutoSize = True, .UseMnemonic = False, .Name = "PhotoDetails"}
    Private ReadOnly message As New Label With {.AutoSize = True, .UseMnemonic = False, .Name = "PhotoStatus"}
    Private ReadOnly viewButton As New Button With {.Text = "View", .AutoSize = True, .Name = "ViewPhoto"}
    Private ReadOnly openButton As New Button With {.Text = "Open", .AutoSize = True, .Name = "OpenPhoto"}
    Private ReadOnly showButton As New Button With {.Text = "Show in folder", .AutoSize = True, .Name = "ShowPhoto"}
    Private ReadOnly copyButton As New Button With {.Text = "Copy", .AutoSize = True, .Name = "CopyPhoto"}
    Private ReadOnly deleteButton As New Button With {.Text = "Delete…", .AutoSize = True, .Name = "DeletePhoto"}
    Private loading As Integer

    Public Sub New()
        Name = "PhotoLibrary" : Margin = New Padding(0)
        Controls.Add(New Label With {.Text = "Photos", .AutoSize = True, .Font = New Font("Segoe UI", 12, FontStyle.Bold)})
        Controls.Add(New Label With {.Text = "Photos you save in Photo Mode (P, or A on a controller) are kept in Pictures\DiRT2VR. Photos taken in VR are saved side by side (SBS, left eye on the left); the viewer can show them side by side, one eye at a time, or swapped for cross-eyed viewing.", .AutoSize = True})
        grid.LargeImageList = images
        Controls.Add(grid) : Controls.Add(detail)
        Dim actions As New FlowLayoutPanel With {.AutoSize = True, .WrapContents = True, .Margin = New Padding(0, 8, 0, 8)}
        Dim refresh As New Button With {.Text = "Refresh", .AutoSize = True, .Name = "RefreshPhotos"}
        Dim folder As New Button With {.Text = "Open folder", .AutoSize = True, .Name = "OpenPhotoFolder"}
        actions.Controls.AddRange({viewButton, openButton, showButton, copyButton, deleteButton, refresh, folder}) : Controls.Add(actions)
        Controls.Add(message)
        AddHandler grid.SelectedIndexChanged, Sub() Details()
        AddHandler grid.ItemActivate, Sub() ShowViewer()
        AddHandler grid.KeyDown, Sub(s, e)
                                     If e.KeyCode = Keys.Delete Then DeleteSelected() : e.Handled = True
                                 End Sub
        AddHandler viewButton.Click, Sub() ShowViewer()
        AddHandler openButton.Click, Sub() Run(Sub() Process.Start(New ProcessStartInfo(Selected.First()) With {.UseShellExecute = True}))
        AddHandler showButton.Click, Sub() Run(Sub() Process.Start("explorer.exe", "/select,""" & Selected.First() & """"))
        AddHandler copyButton.Click, Sub() Run(Sub()
                                                   Using photo = PhotoLibrary.Load(Selected.First())
                                                       Clipboard.SetImage(photo)
                                                   End Using
                                                   message.Text = "Copied to the clipboard."
                                               End Sub)
        AddHandler deleteButton.Click, Sub() DeleteSelected()
        AddHandler refresh.Click, Async Sub() Await ReloadAsync()
        AddHandler folder.Click, Sub() Run(Sub()
                                               Directory.CreateDirectory(PhotoLibrary.Folder)
                                               Process.Start(New ProcessStartInfo(PhotoLibrary.Folder) With {.UseShellExecute = True})
                                           End Sub)
        Details()
    End Sub

    Private ReadOnly Property Selected As List(Of String)
        Get
            Return grid.SelectedItems.Cast(Of ListViewItem)().Select(Function(i) CStr(i.Tag)).ToList()
        End Get
    End Property

    Private Sub Run(action As Action)
        Try
            action()
        Catch ex As Exception
            message.Text = ex.Message
        End Try
    End Sub

    Public Async Function ReloadAsync() As Tasks.Task
        Dim generation = Threading.Interlocked.Increment(loading)
        Dim keep = Selected
        message.Text = "Loading photos…"
        Dim files As List(Of IO.FileInfo)
        Try
            files = Await Tasks.Task.Run(Function() PhotoLibrary.Items())
        Catch ex As Exception
            message.Text = ex.Message : Return
        End Try
        If generation <> loading Then Return
        grid.BeginUpdate()
        Try
            grid.Items.Clear()
            For Each image As Image In images.Images : image.Dispose() : Next
            images.Images.Clear()
            For Each file In files
                Dim item As New ListViewItem(file.LastWriteTime.ToString("g") & If(PhotoLibrary.IsStereo(file.FullName), "  ·  SBS", "")) With {.Tag = file.FullName, .ImageIndex = -1}
                grid.Items.Add(item)
                If keep.Contains(file.FullName) Then item.Selected = True
            Next
            If grid.SelectedItems.Count = 0 AndAlso grid.Items.Count > 0 Then grid.Items(0).Selected = True
        Finally
            grid.EndUpdate()
        End Try
        message.Text = If(files.Count = 0, "No photos yet. Pause a race or a replay, choose PHOTO MODE and press P.", $"{files.Count} photo(s).")
        Details()
        ' Thumbnails in the background, newest first.
        For i = 0 To files.Count - 1
            Dim path = files(i).FullName
            Dim thumb As Bitmap = Nothing
            Try
                thumb = Await Tasks.Task.Run(Function() PhotoLibrary.Thumbnail(path, ThumbSize))
            Catch
                Continue For ' A damaged or partly written photo keeps a blank tile.
            End Try
            If generation <> loading OrElse IsDisposed Then thumb.Dispose() : Return
            images.Images.Add(path, thumb)
            Dim item = grid.Items.Cast(Of ListViewItem)().FirstOrDefault(Function(x) CStr(x.Tag) = path)
            If item IsNot Nothing Then item.ImageKey = path
        Next
    End Function

    Private Sub Details()
        Dim paths = Selected
        Dim one = paths.Count = 1
        viewButton.Enabled = paths.Count > 0 : openButton.Enabled = one : showButton.Enabled = one : copyButton.Enabled = one
        deleteButton.Enabled = paths.Count > 0
        If paths.Count = 0 Then
            detail.Text = "Choose a photo."
        ElseIf Not one Then
            detail.Text = $"{paths.Count} photos selected."
        Else
            Try
                Dim info As New IO.FileInfo(paths(0))
                Dim size = ""
                Using stream = File.OpenRead(paths(0)), image = Drawing.Image.FromStream(stream, False, False)
                    size = $"{image.Width} × {image.Height}"
                End Using
                detail.Text = info.Name & vbCrLf & $"{info.LastWriteTime:g}  ·  {size}  ·  {info.Length / 1048576.0:F1} MB" &
                    If(PhotoLibrary.IsStereo(paths(0)), "  ·  VR side by side", "")
            Catch ex As Exception
                detail.Text = IO.Path.GetFileName(paths(0)) & " — " & ex.Message
            End Try
        End If
    End Sub

    Private Sub ShowViewer()
        Dim paths = grid.Items.Cast(Of ListViewItem)().Select(Function(i) CStr(i.Tag)).ToList()
        If Selected.Count = 0 OrElse paths.Count = 0 Then Return
        Using viewer As New PhotoViewer(paths, paths.IndexOf(Selected.First()))
            viewer.ShowDialog(FindForm())
            If viewer.Deleted Then
                Dim reload = ReloadAsync()
            End If
        End Using
    End Sub

    Private Sub DeleteSelected()
        Dim paths = Selected
        If paths.Count = 0 Then Return
        Dim question = If(paths.Count = 1, "Move “" & IO.Path.GetFileName(paths(0)) & "” to the Recycle Bin?", $"Move {paths.Count} photos to the Recycle Bin?")
        If MessageBox.Show(Me, question, "Delete photos", MessageBoxButtons.YesNo, MessageBoxIcon.Question) <> DialogResult.Yes Then Return
        Try
            For Each path In paths : PhotoLibrary.Recycle(path) : Next
            message.Text = If(paths.Count = 1, "Photo moved to the Recycle Bin.", $"{paths.Count} photos moved to the Recycle Bin.")
        Catch ex As Exception
            message.Text = ex.Message
        End Try
        Dim reload = ReloadAsync()
    End Sub

    Protected Overrides Sub OnSizeChanged(e As EventArgs)
        For Each label In Controls.OfType(Of Label)()
            label.MaximumSize = New Size(Math.Max(1, ClientSize.Width - label.Margin.Horizontal), 0)
        Next
        grid.Width = Math.Max(200, ClientSize.Width - grid.Margin.Horizontal)
        MyBase.OnSizeChanged(e)
    End Sub
End Class

' A simple full-window viewer: previous/next, fullscreen, and the stereo views.
Public Class PhotoViewer
    Inherits Form
    Private deletedAny As Boolean
    Public ReadOnly Property Deleted As Boolean
        Get
            Return deletedAny
        End Get
    End Property
    Private ReadOnly paths As List(Of String)
    Private index As Integer
    Private photo As Bitmap
    Private ReadOnly picture As New PictureBox With {.Dock = DockStyle.Fill, .SizeMode = PictureBoxSizeMode.Zoom, .BackColor = Color.Black, .Name = "PhotoView"}
    Private ReadOnly stereo As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 210, .Name = "StereoView", .AccessibleName = "Stereo view"}
    Private ReadOnly caption As New Label With {.AutoSize = True, .UseMnemonic = False, .Margin = New Padding(12, 8, 12, 0), .Name = "PhotoCaption"}

    Public Sub New(list As List(Of String), start As Integer)
        paths = list : index = Math.Max(0, start)
        Text = "DiRT2VR photos" : StartPosition = FormStartPosition.CenterParent : KeyPreview = True
        Size = New Size(1280, 800) : MinimumSize = New Size(640, 400) : BackColor = Color.Black
        stereo.Items.AddRange({"Side by side", "Left eye", "Right eye", "Cross-eyed (swapped)"}) : stereo.SelectedIndex = 0
        Dim bar As New FlowLayoutPanel With {.Dock = DockStyle.Bottom, .AutoSize = True, .WrapContents = True, .Padding = New Padding(8)}
        Dim previous As New Button With {.Text = "‹ Previous", .AutoSize = True, .Name = "PreviousPhoto"}
        Dim [next] As New Button With {.Text = "Next ›", .AutoSize = True, .Name = "NextPhoto"}
        Dim full As New Button With {.Text = "Full screen", .AutoSize = True, .Name = "FullScreenPhoto"}
        Dim open As New Button With {.Text = "Open", .AutoSize = True}
        Dim delete As New Button With {.Text = "Delete…", .AutoSize = True}
        Dim close As New Button With {.Text = "Close", .AutoSize = True}
        bar.Controls.AddRange({previous, [next], stereo, full, open, delete, close, caption})
        Controls.Add(picture) : Controls.Add(bar)
        LauncherAppearance.Apply(bar)
        AddHandler previous.Click, Sub() Turn(-1)
        AddHandler [next].Click, Sub() Turn(1)
        AddHandler stereo.SelectedIndexChanged, Sub() ShowCurrent(False)
        AddHandler full.Click, Sub() ToggleFullScreen()
        AddHandler open.Click, Sub() Process.Start(New ProcessStartInfo(paths(index)) With {.UseShellExecute = True})
        AddHandler delete.Click, Sub() DeleteCurrent()
        AddHandler close.Click, Sub() Me.Close()
        AddHandler picture.DoubleClick, Sub() ToggleFullScreen()
        ShowCurrent(True)
    End Sub

    Protected Overrides Sub OnKeyDown(e As KeyEventArgs)
        Select Case e.KeyCode
            Case Keys.Left, Keys.PageUp : Turn(-1) : e.Handled = True
            Case Keys.Right, Keys.PageDown, Keys.Space : Turn(1) : e.Handled = True
            Case Keys.F11, Keys.F : ToggleFullScreen() : e.Handled = True
            Case Keys.Delete : DeleteCurrent() : e.Handled = True
            Case Keys.Escape
                If FormBorderStyle = FormBorderStyle.None Then ToggleFullScreen() Else Close()
                e.Handled = True
        End Select
        MyBase.OnKeyDown(e)
    End Sub

    Private Sub Turn(direction As Integer)
        If paths.Count = 0 Then Return
        index = (index + direction + paths.Count) Mod paths.Count
        ShowCurrent(True)
    End Sub

    Private Sub ShowCurrent(load As Boolean)
        If paths.Count = 0 Then Close() : Return
        Try
            If load Then photo?.Dispose() : photo = PhotoLibrary.Load(paths(index))
            Dim isStereo = PhotoLibrary.IsStereo(paths(index))
            stereo.Visible = isStereo
            Dim old = picture.Image
            picture.Image = If(isStereo, PhotoLibrary.View(photo, CType(stereo.SelectedIndex, StereoView)), New Bitmap(photo))
            old?.Dispose()
            caption.Text = $"{index + 1} / {paths.Count}   {IO.Path.GetFileName(paths(index))}"
        Catch ex As Exception
            caption.Text = IO.Path.GetFileName(paths(index)) & " — " & ex.Message
        End Try
    End Sub

    Private Sub ToggleFullScreen()
        If FormBorderStyle = FormBorderStyle.None Then
            FormBorderStyle = FormBorderStyle.Sizable : WindowState = FormWindowState.Normal
        Else
            FormBorderStyle = FormBorderStyle.None : WindowState = FormWindowState.Maximized
        End If
    End Sub

    Private Sub DeleteCurrent()
        If paths.Count = 0 Then Return
        If MessageBox.Show(Me, "Move “" & IO.Path.GetFileName(paths(index)) & "” to the Recycle Bin?", "Delete photo", MessageBoxButtons.YesNo, MessageBoxIcon.Question) <> DialogResult.Yes Then Return
        Try
            Dim old = picture.Image : picture.Image = Nothing : old?.Dispose()
            photo?.Dispose() : photo = Nothing
            PhotoLibrary.Recycle(paths(index))
            paths.RemoveAt(index) : deletedAny = True
            If index >= paths.Count Then index = paths.Count - 1
            ShowCurrent(True)
        Catch ex As Exception
            caption.Text = ex.Message
        End Try
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then picture.Image?.Dispose() : photo?.Dispose()
        MyBase.Dispose(disposing)
    End Sub
End Class
