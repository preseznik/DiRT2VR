Imports System.Drawing
Imports System.Windows.Forms
Imports DiRT2VR.Profiles

Public Class ProfilePanel
    Inherits VerticalStack
    Private ReadOnly context As InstallContext
    Private ReadOnly service As ProfileService
    Private ReadOnly careers As New ListBox With {.Name = "CareerList", .AccessibleName = "Careers", .IntegralHeight = False, .Height = 160, .DrawMode = DrawMode.OwnerDrawFixed}
    Private ReadOnly selectedLabel As New Label With {.Name = "SelectedCareer", .AutoSize = True, .UseMnemonic = False}
    Private ReadOnly detailLabels As New Dictionary(Of String, Label)
    Private ReadOnly message As New Label With {.Name = "ProfileMessage", .AutoSize = True, .UseMnemonic = False}
    Private ReadOnly useButton As New Button With {.Text = "Use this profile", .Name = "UseProfile", .AutoSize = True}
    Private ReadOnly deleteButton As New Button With {.Text = "Delete profile…", .Name = "DeleteProfile", .AutoSize = True}
    Private selectedId As String
    Private reloading As Boolean

    Private Class DetailRow
        Inherits Panel
        Private ReadOnly caption As Label
        Private ReadOnly value As Label
        Private arranging As Boolean
        Public Sub New(title As String, label As Label)
            Margin = New Padding(0)
            caption = New Label With {.Text = title, .AutoSize = True}
            value = label
            Controls.Add(caption) : Controls.Add(value)
        End Sub
        Protected Overrides Sub OnLayout(e As LayoutEventArgs)
            MyBase.OnLayout(e)
            If arranging OrElse caption Is Nothing Then Return
            arranging = True
            Try
                Dim gap = Px(Me, 12), pad = Px(Me, 5)
                Dim left = Math.Min(Px(Me, 160), ClientSize.Width \ 2)
                caption.MaximumSize = New Size(Math.Max(1, left - gap), 0)
                value.MaximumSize = New Size(Math.Max(1, ClientSize.Width - left), 0)
                caption.Location = New Point(0, pad) : value.Location = New Point(left, pad)
                Height = Math.Max(caption.PreferredHeight, value.PreferredHeight) + pad * 2
            Finally
                arranging = False
            End Try
        End Sub
    End Class
    Private Class CareerItem
        Public Id As String
        Public Caption As String
        Public Details As ProfileDetails
        Public Problem As String
        Public Overrides Function ToString() As String
            Return Caption
        End Function
    End Class

    Public Sub New(value As InstallContext)
        context = value : service = New ProfileService(context)
        Name = "Profiles" : Margin = New Padding(0)
        Controls.Add(selectedLabel)
        Controls.Add(careers)
        careers.ItemHeight = TextRenderer.MeasureText("Ag", careers.Font).Height + Px(careers, 8)
        AddHandler careers.FontChanged, Sub() careers.ItemHeight = TextRenderer.MeasureText("Ag", careers.Font).Height + Px(careers, 8)
        AddHandler careers.DpiChangedAfterParent, Sub() careers.ItemHeight = TextRenderer.MeasureText("Ag", careers.Font).Height + Px(careers, 8)
        AddHandler careers.DrawItem, AddressOf DrawCareer
        Dim commands As New FlowLayoutPanel With {.AutoSize = True, .WrapContents = True, .Margin = New Padding(0, 8, 0, 8)}
        Dim refresh As New Button With {.Text = "Refresh", .Name = "RefreshProfiles", .AutoSize = True}
        Dim create As New Button With {.Text = "Create new profile…", .Name = "CreateProfile", .AutoSize = True}
        commands.Controls.AddRange({useButton, refresh, create, deleteButton}) : Controls.Add(commands)
        For Each title In {"Name", "Completion", "Level", "Money", "Cars owned", "Last saved"}
            Dim label As New Label With {.Name = "Profile" & title.Replace(" ", ""), .Text = "Unavailable", .AutoSize = True, .UseMnemonic = False}
            detailLabels.Add(title, label) : Controls.Add(New DetailRow(title, label))
        Next
        Controls.Add(message)
        AddHandler careers.SelectedIndexChanged, Sub() If Not reloading Then ShowDetails()
        AddHandler refresh.Click, Sub() Reload()
        AddHandler useButton.Click, Sub() ChooseCareer()
        AddHandler create.Click, Sub() CreateCareer()
        AddHandler deleteButton.Click, Sub() DeleteCareer()
        Reload()
    End Sub

    Private Sub DrawCareer(sender As Object, e As DrawItemEventArgs)
        If e.Index < 0 OrElse e.Index >= careers.Items.Count Then Return
        Dim item = DirectCast(careers.Items(e.Index), CareerItem)
        Dim active = item.Id = selectedId
        Dim selected = (e.State And DrawItemState.Selected) <> 0
        Dim background = If(selected, SystemColors.Highlight, careers.BackColor)
        Dim foreground = If(selected, SystemColors.HighlightText, careers.ForeColor)
        If selected AndAlso Not SystemInformation.HighContrast AndAlso careers.BackColor.GetBrightness() < 0.5F Then
            background = Color.FromArgb(48, 89, 142) : foreground = Color.White
        End If
        If active AndAlso Not SystemInformation.HighContrast Then
            Dim dark = careers.BackColor.GetBrightness() < 0.5F
            background = If(dark, Color.FromArgb(43, 72, 37), Color.FromArgb(222, 241, 205))
            foreground = If(dark, Color.FromArgb(224, 251, 197), Color.FromArgb(35, 75, 20))
        End If
        Using brush As New SolidBrush(background)
            e.Graphics.FillRectangle(brush, e.Bounds)
        End Using
        Dim bounds = e.Bounds
        bounds.Inflate(-Px(careers, 6), 0)
        TextRenderer.DrawText(e.Graphics, item.Caption & If(active, " — Active", ""), e.Font, bounds, foreground,
            TextFormatFlags.Left Or TextFormatFlags.VerticalCenter Or TextFormatFlags.EndEllipsis Or TextFormatFlags.NoPrefix)
        If (e.State And DrawItemState.Focus) <> 0 Then ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(e.Bounds, -2, -2), foreground, background)
    End Sub

    Protected Overrides Sub OnSizeChanged(e As EventArgs)
        ' Keep text inside the tab when it wraps in portrait windows or at high DPI.
        If selectedLabel IsNot Nothing Then selectedLabel.MaximumSize = New Size(Math.Max(1, ClientSize.Width - selectedLabel.Margin.Horizontal), 0)
        If message IsNot Nothing Then message.MaximumSize = New Size(Math.Max(1, ClientSize.Width - message.Margin.Horizontal), 0)
        MyBase.OnSizeChanged(e)
    End Sub

    Public Sub Reload(Optional highlight As String = Nothing)
        reloading = True : careers.BeginUpdate()
        Try
            Dim previous = TryCast(careers.SelectedItem, CareerItem)
            Dim warning As String = Nothing
            selectedId = Nothing
            Try
                selectedId = service.SelectedId()
            Catch ex As Exception
                warning = "The selected profile is unavailable. Choose another profile explicitly before launching. " & ex.Message
            End Try
            careers.Items.Clear()
            Dim current As New CareerItem With {.Id = ProfileStore.CurrentCareer, .Caption = "Current game career"}
            Try
                Dim saveRoot = IO.Path.Combine(IO.Path.GetDirectoryName(IO.Path.GetDirectoryName(context.GraphicsPath)), "savegame")
                If Directory.Exists(saveRoot) Then
                    Dim saves = Directory.GetDirectories(saveRoot, "Autosave*").Where(Function(p) System.Text.RegularExpressions.Regex.IsMatch(IO.Path.GetFileName(p), "^Autosave[0-9]+$")).ToArray()
                    If saves.Length = 1 Then
                        current.Details = ProfileDetails.Inspect(saves(0))
                    ElseIf saves.Length > 1 Then
                        current.Problem = "Several careers are saved in the game. Choose between them in the game's Load Profile menu."
                    End If
                End If
            Catch ex As Exception
                current.Problem = "Career details are unavailable. " & ex.Message
            End Try
            careers.Items.Add(current)
            For Each entry In service.Store.List()
                Dim item As New CareerItem With {.Id = entry.Id, .Caption = If(entry.Info?.Name, "Unavailable profile"), .Problem = entry.Error}
                If entry.Info IsNot Nothing Then
                    item.Details = ProfileDetails.Inspect(IO.Path.Combine(service.Store.ProfileRoot(entry.Id), "savegame", "Autosave0"))
                    If Not String.IsNullOrWhiteSpace(item.Details.Name) Then item.Caption = item.Details.Name
                End If
                careers.Items.Add(item)
            Next
            Dim active = careers.Items.Cast(Of CareerItem)().FirstOrDefault(Function(item) item.Id = selectedId)
            selectedLabel.Text = "Used for all launches: " & If(active?.Caption, "Unavailable — select a profile")
            Dim focus = If(highlight, If(previous?.Id, selectedId))
            careers.SelectedItem = careers.Items.Cast(Of CareerItem)().FirstOrDefault(Function(item) item.Id = focus)
            If careers.SelectedIndex < 0 Then careers.SelectedIndex = 0
            ShowDetails()
            If warning IsNot Nothing Then message.Text = warning
        Catch ex As Exception
            message.Text = "Could not refresh profiles. " & ex.Message
            useButton.Enabled = False
        Finally
            reloading = False : careers.EndUpdate() : careers.Invalidate()
        End Try
    End Sub

    Private Sub ShowDetails()
        Dim item = TryCast(careers.SelectedItem, CareerItem)
        For Each label In detailLabels.Values
            label.Text = "Unavailable"
        Next
        useButton.Enabled = item IsNot Nothing AndAlso item.Id <> selectedId AndAlso (item.Id = ProfileStore.CurrentCareer OrElse item.Problem Is Nothing)
        deleteButton.Enabled = item IsNot Nothing AndAlso item.Id <> ProfileStore.CurrentCareer
        If item Is Nothing Then Return
        Dim details = item.Details
        If details IsNot Nothing Then
            detailLabels("Name").Text = If(String.IsNullOrWhiteSpace(details.Name), "Unavailable", details.Name)
            If details.Completion.HasValue Then detailLabels("Completion").Text = details.Completion.Value.ToString() & "%"
            If details.Level.HasValue Then detailLabels("Level").Text = details.Level.Value.ToString()
            If details.Money.HasValue Then detailLabels("Money").Text = details.Money.Value.ToString("N0") & " credits"
            If details.CarsOwned.HasValue Then detailLabels("Cars owned").Text = details.CarsOwned.Value.ToString()
            If details.LastSavedUtc.HasValue Then detailLabels("Last saved").Text = details.LastSavedUtc.Value.ToLocalTime().ToString("g")
        End If
        message.Text = If(item.Problem, "Graphics settings and launcher controls are shared. Existing careers stay unchanged when you create a new one.")
    End Sub

    Private Sub ChooseCareer()
        Dim item = TryCast(careers.SelectedItem, CareerItem)
        If item Is Nothing Then Return
        Try
            service.SelectProfile(item.Id)
            Reload(item.Id)
        Catch ex As Exception
            message.Text = "Could not select this profile. " & ex.Message
        End Try
    End Sub

    Private Sub DeleteCareer()
        Dim item = TryCast(careers.SelectedItem, CareerItem)
        If item Is Nothing OrElse item.Id = ProfileStore.CurrentCareer Then Return
        If item.Id = selectedId Then
            message.Text = "Choose another career and click Use this profile before deleting this one."
            Return
        End If
        If MessageBox.Show(FindForm(), "Permanently delete " & ChrW(34) & item.Caption & ChrW(34) & " and all its saves?" & vbCrLf & vbCrLf &
            "This removes it from all DiRT2VR installations on this Windows account. This cannot be undone.",
            "Delete profile", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) <> DialogResult.Yes Then Return
        Try
            service.DeleteProfile(item.Id)
            Reload(selectedId)
            message.Text = "Profile deleted."
        Catch ex As Exception
            message.Text = "Could not delete this profile. " & ex.Message
        End Try
    End Sub

    Private Sub CreateCareer()
        Using dialog As New CreateProfileForm(service)
            If dialog.ShowDialog(FindForm()) = DialogResult.OK Then
                Reload(dialog.CreatedId)
                message.Text = "Profile created and active for your next launch."
            End If
        End Using
    End Sub
End Class

Public Class CreateProfileForm
    Inherits LauncherForm
    Private ReadOnly profileName As New TextBox With {.Name = "NewProfileName", .AccessibleName = "Profile name", .MaxLength = 24}
    Private ReadOnly fresh As New RadioButton With {.Text = "Fresh career", .Name = "FreshCareer", .Checked = True, .AutoSize = True}
    Private ReadOnly completed As New RadioButton With {.Text = "100% completed career", .Name = "CompletedCareer", .AutoSize = True}
    Private ReadOnly problem As New Label With {.Name = "CreationError", .AutoSize = True, .UseMnemonic = False}
    Public CreatedId As String
    Public Sub New(service As ProfileService)
        Text = "Create new profile" : Font = New Font("Segoe UI", 10)
        AutoScaleMode = AutoScaleMode.Dpi : StartPosition = FormStartPosition.CenterParent
        FormBorderStyle = FormBorderStyle.FixedDialog : MaximizeBox = False : MinimizeBox = False
        ClientSize = New Size(460, 340)
        Dim content = Stack() : content.Padding = New Padding(20)
        content.Controls.Add(New Label With {.Text = "Profile name", .AutoSize = True})
        content.Controls.Add(profileName)
        content.Controls.Add(New Label With {.Text = "1–24 letters (A–Z) or numbers (0–9). No spaces or symbols.", .AutoSize = True, .MaximumSize = New Size(420, 0), .Name = "ProfileNameHint"})
        content.Controls.Add(fresh) : content.Controls.Add(completed)
        content.Controls.Add(New Label With {.Text = "Creates a separate career. Your existing saves are not changed.", .AutoSize = True, .MaximumSize = New Size(420, 0), .Margin = New Padding(0, 12, 0, 12)})
        Dim commands As New FlowLayoutPanel With {.AutoSize = True, .WrapContents = True}
        Dim create As New Button With {.Text = "Create", .Name = "ConfirmCreateProfile", .AutoSize = True}
        Dim cancel As New Button With {.Text = "Cancel", .AutoSize = True, .DialogResult = DialogResult.Cancel}
        commands.Controls.AddRange({create, cancel}) : content.Controls.Add(commands)
        problem.MaximumSize = New Size(420, 0) : content.Controls.Add(problem) : Controls.Add(content)
        AcceptButton = create : CancelButton = cancel
        create.Enabled = False
        AddHandler profileName.KeyPress, Sub(sender, e)
                                            If Not Char.IsControl(e.KeyChar) AndAlso Not ProfileStore.IsValidNewName(e.KeyChar.ToString()) Then
                                                e.Handled = True
                                                problem.Text = "Use letters (A-Z) and numbers (0-9) only."
                                            End If
                                        End Sub
        AddHandler profileName.TextChanged, Sub()
                                               create.Enabled = ProfileStore.IsValidNewName(profileName.Text)
                                               problem.Text = If(create.Enabled OrElse profileName.Text.Length = 0, "", "Use letters (A-Z) and numbers (0-9) only.")
                                           End Sub
        AddHandler create.Click, Sub()
                                     Try
                                         UseWaitCursor = True
                                         Dim created = service.Create(profileName.Text, completed.Checked)
                                         CreatedId = created.Id : DialogResult = DialogResult.OK
                                     Catch ex As Exception
                                         problem.Text = ex.Message
                                     Finally
                                         UseWaitCursor = False
                                     End Try
                                 End Sub
        AddHandler content.SizeChanged, Sub() ClientSize = New Size(ClientSize.Width, Math.Max(Px(Me, 300), content.Height))
    End Sub
End Class
