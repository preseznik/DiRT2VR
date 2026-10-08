Imports System.Drawing
Imports System.Windows.Forms

' The editor owns a draft; launch selections remain independent of what is being edited.
Public Class FilterPanel
    Inherits UserControl
    Private ReadOnly store As FilterStore, settings As VrSettings
    Private ReadOnly desktop As New ComboBox With {.Name = "DesktopFilter", .DropDownStyle = ComboBoxStyle.DropDownList}
    Private ReadOnly headset As New ComboBox With {.Name = "VrFilter", .DropDownStyle = ComboBoxStyle.DropDownList}
    Private ReadOnly editor As New ComboBox With {.Name = "EditFilter", .DropDownStyle = ComboBoxStyle.DropDownList}
    Private ReadOnly lighting As New ComboBox With {.Name = "FilterLighting", .DropDownStyle = ComboBoxStyle.DropDownList}
    Private ReadOnly note As New Label With {.AutoSize = True, .UseMnemonic = False}
    Private ReadOnly tint As New CheckBox With {.Text = "Override", .AutoSize = True}
    Private ReadOnly tintColour As New Button With {.AutoSize = True}
    Private ReadOnly tintStrength As New ValueSlider("FilterTintStrength", 0, 100, 0, "%")
    Private ReadOnly sliders As New Dictionary(Of String, ValueSlider)
    Private ReadOnly editableButtons As New List(Of Button)
    Private ReadOnly tips As New ToolTip With {.AutoPopDelay = 15000}
    Private draft As FilterPreset = New FilterPreset(), baseline As String
    Private loading As Boolean, dirty As Boolean

    Public Sub New(context As InstallContext, preferences As VrSettings)
        settings = preferences : store = New FilterStore(context)
        Name = "FiltersPanel" : Dock = DockStyle.Fill : AutoScroll = True
        Dim content = Stack() : Controls.Add(content)
        Dim selection = Section(content, "Use on next launch")
        For Each box In {desktop, headset, editor, lighting}
            StyleChoice(box, Me)
        Next
        Field(selection, "Desktop preset", desktop) : Field(selection, "VR preset", headset)
        selection.Controls.Add(New Label With {.Text = "Changes apply on the next launch. Main's Bloom switches still apply.", .AutoSize = True})
        Dim editing = Section(content, "Preset editor")
        Field(editing, "Edit preset", editor)
        Dim actions As New FlowLayoutPanel With {.AutoSize = True, .WrapContents = True}
        editing.Controls.Add(actions)
        AddButton(actions, "New", Sub() CreateCopy(False))
        AddButton(actions, "Save as", Sub() CreateCopy(True))
        editableButtons.Add(AddButton(actions, "Rename", AddressOf RenamePreset))
        editableButtons.Add(AddButton(actions, "Delete", AddressOf DeletePreset))
        editableButtons.Add(AddButton(actions, "Reset adjustments", Sub()
                                                                      If lighting.SelectedIndex = 1 Then
                                                                          draft.Night = New FilterVariant()
                                                                      Else
                                                                          draft.Day = New FilterVariant()
                                                                      End If
                                                                      MarkDirty() : ShowVariant()
                                                                  End Sub))
        AddButton(actions, "Import…", AddressOf ImportPreset)
        AddButton(actions, "Export…", AddressOf ExportPreset)
        editing.Controls.Add(note)
        lighting.Items.AddRange({"Day", "Night"}) : lighting.SelectedIndex = 0
        Field(editing, "Lighting", lighting)
        Dim columns As New ResponsiveColumns() : content.Controls.Add(columns)
        For Each group In FilterParameters.All.Select(Function(p) p.Group).Distinct()
            Dim sectionPanel = Section(If(group = "Colour" OrElse group = "Bloom", columns.First, columns.Second), group)
            For Each parameter In FilterParameters.All.Where(Function(p) p.Group = group)
                Dim control As New ValueSlider("Filter_" & parameter.Key, parameter.Minimum, parameter.Maximum, parameter.DefaultValue, "%", If(parameter.Key = "brightness", 10D, 1D))
                sliders.Add(parameter.Key, control) : Field(sectionPanel, parameter.Caption, control)
                AddTip(control, parameter.Help)
                AddHandler control.ValueChanged, Sub()
                                                     If loading Then Return
                                                     CurrentVariant.Values(parameter.Key) = control.Value : MarkDirty()
                                                 End Sub
            Next
            If group = "Colour" Then
                Field(sectionPanel, "Image tint", tint) : Field(sectionPanel, "Tint colour", tintColour) : Field(sectionPanel, "Tint strength", tintStrength)
            End If
            If group = "Motion blur" Then sectionPanel.Controls.Add(New Label With {.Text = "Unavailable in VR: motion blur stays disabled.", .AutoSize = True})
        Next
        AddHandler lighting.SelectedIndexChanged, Sub() ShowVariant()
        AddHandler editor.SelectedIndexChanged, AddressOf ChangeEditor
        AddHandler tint.CheckedChanged, Sub()
                                            If loading Then Return
                                            CurrentVariant.TintEnabled = tint.Checked : MarkDirty() : TintState()
                                        End Sub
        AddHandler tintStrength.ValueChanged, Sub()
                                                 If loading Then Return
                                                 CurrentVariant.TintStrength = tintStrength.Value : MarkDirty()
                                             End Sub
        AddHandler tintColour.Click, Sub()
                                         Using picker As New ColorDialog With {.Color = ColorTranslator.FromHtml("#" & CurrentVariant.TintRgb), .FullOpen = True}
                                             If picker.ShowDialog(Me) <> DialogResult.OK Then Return
                                             CurrentVariant.TintRgb = picker.Color.R.ToString("X2") & picker.Color.G.ToString("X2") & picker.Color.B.ToString("X2")
                                             MarkDirty() : ShowVariant()
                                         End Using
                                     End Sub
        ReloadLibrary("original", settings.DesktopFilterId, settings.VrFilterId)
    End Sub
    Private ReadOnly Property CurrentVariant As FilterVariant
        Get
            Return If(lighting.SelectedIndex = 1, draft.Night, draft.Day)
        End Get
    End Property
    Private Sub AddTip(control As Control, description As String)
        tips.SetToolTip(control, description)
        For Each child As Control In control.Controls
            AddTip(child, description)
        Next
    End Sub
    Private Function AddButton(parent As Control, caption As String, action As Action) As Button
        Dim button As New Button With {.Text = caption, .AutoSize = True}
        AddHandler button.Click, Sub()
                                     Try
                                         action()
                                     Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
                                         MessageBox.Show(Me, ex.Message, "Filters", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                                     End Try
                                 End Sub
        parent.Controls.Add(button) : Return button
    End Function
    Private Shared Function SelectedId(box As ComboBox) As String
        Return TryCast(box.SelectedItem, FilterPreset)?.Id
    End Function
    Private Sub ReloadLibrary(editId As String, Optional desktopId As String = Nothing, Optional vrId As String = Nothing)
        desktopId = If(desktopId, SelectedId(desktop)) : vrId = If(vrId, SelectedId(headset))
        loading = True
        Try
            Dim warning = "", presets As List(Of FilterPreset)
            Try
                presets = store.List(warning)
            Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
                warning = ex.Message : presets = New List(Of FilterPreset) From {New FilterPreset()}
            End Try
            For Each box In {desktop, headset, editor}
                Dim id = If(box Is desktop, desktopId, If(box Is headset, vrId, editId))
                box.BeginUpdate() : box.Items.Clear() : box.Items.AddRange(presets.Cast(Of Object)().ToArray())
                Dim selected = presets.FirstOrDefault(Function(p) p.Id = id)
                If selected Is Nothing AndAlso box IsNot editor Then
                    selected = New FilterPreset With {.Id = id, .Name = "Unavailable — choose a preset"} : box.Items.Add(selected)
                End If
                box.SelectedItem = If(selected, presets(0)) : box.EndUpdate()
            Next
            LoadDraft(DirectCast(editor.SelectedItem, FilterPreset))
            If warning.Length > 0 Then note.Text = "Some preset files could not be read. " & warning
        Finally
            loading = False
        End Try
        ShowVariant()
    End Sub
    Private Sub LoadDraft(preset As FilterPreset)
        draft = preset.Copy() : baseline = System.Text.Json.JsonSerializer.Serialize(draft, FilterStore.Json) : dirty = False
        UpdateNote()
    End Sub
    Private Sub ChangeEditor(sender As Object, e As EventArgs)
        If loading OrElse editor.SelectedItem Is Nothing Then Return
        Dim nextPreset = DirectCast(editor.SelectedItem, FilterPreset)
        Try
            If Not LeaveDraft() Then
                loading = True : editor.SelectedItem = editor.Items.Cast(Of FilterPreset)().First(Function(p) p.Id = draft.Id) : loading = False : Return
            End If
            LoadDraft(store.Load(nextPreset.Id)) : ShowVariant()
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
            loading = True : editor.SelectedItem = editor.Items.Cast(Of FilterPreset)().First(Function(p) p.Id = draft.Id) : loading = False
            MessageBox.Show(Me, ex.Message, "Filters", MessageBoxButtons.OK, MessageBoxIcon.Warning)
        End Try
    End Sub
    Private Function LeaveDraft() As Boolean
        If Not dirty Then Return True
        Select Case MessageBox.Show(Me, "Save changes to " & draft.Name & "?" & vbCrLf & "Yes: save. No: discard. Cancel: keep editing.", "Unsaved filter", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question)
            Case DialogResult.Yes : SaveDraft() : Return True
            Case DialogResult.No : Return True
            Case Else : Return False
        End Select
    End Function
    Private Sub SaveDraft()
        If Not dirty Then Return
        Dim current = System.Text.Json.JsonSerializer.Serialize(store.Load(draft.Id), FilterStore.Json)
        If current <> baseline Then Throw New IOException("This preset changed in another launcher. Export your adjustments, then reselect it before editing again.")
        store.Save(draft) : baseline = System.Text.Json.JsonSerializer.Serialize(draft, FilterStore.Json) : dirty = False : UpdateNote()
    End Sub
    Private Sub MarkDirty()
        dirty = True : UpdateNote()
    End Sub
    Private Sub UpdateNote()
        note.Text = If(draft.Id = "original", "Original is read-only. Choose New or Save as to adjust it.", If(dirty, "Unsaved adjustments — use Save settings to keep them.", "Shared preset. Save settings keeps your adjustments and launch selections."))
    End Sub
    Private Sub TintState()
        tintColour.Enabled = draft.Id <> "original" AndAlso tint.Checked
        tintStrength.Enabled = tintColour.Enabled
    End Sub
    Private Sub ShowVariant()
        Dim wasLoading = loading : loading = True
        Try
            For Each parameter In FilterParameters.All
                sliders(parameter.Key).Value = CurrentVariant.Value(parameter)
                sliders(parameter.Key).Enabled = draft.Id <> "original"
            Next
            tint.Checked = CurrentVariant.TintEnabled : tint.Enabled = draft.Id <> "original"
            tintColour.Text = "#" & CurrentVariant.TintRgb : tintStrength.Value = CurrentVariant.TintStrength : TintState()
            For Each button In editableButtons
                button.Enabled = draft.Id <> "original"
            Next
        Finally
            loading = wasLoading
        End Try
    End Sub
    Private Function AskName(initial As String) As String
        Using dialog As New Form With {.Text = "Preset name", .StartPosition = FormStartPosition.CenterParent, .ClientSize = New Size(400, 115), .MinimizeBox = False, .MaximizeBox = False, .FormBorderStyle = FormBorderStyle.FixedDialog}
            Dim nameBox As New TextBox With {.Text = initial, .MaxLength = 64, .Dock = DockStyle.Top}
            Dim actions As New FlowLayoutPanel With {.Dock = DockStyle.Bottom, .Height = 42, .FlowDirection = FlowDirection.RightToLeft}
            Dim cancel As New Button With {.Text = "Cancel", .DialogResult = DialogResult.Cancel}, ok As New Button With {.Text = "OK", .DialogResult = DialogResult.OK}
            actions.Controls.Add(cancel) : actions.Controls.Add(ok) : dialog.Controls.Add(actions) : dialog.Controls.Add(nameBox)
            dialog.Padding = New Padding(12) : dialog.AcceptButton = ok : dialog.CancelButton = cancel
            LauncherAppearance.Apply(dialog)
            If dialog.ShowDialog(Me) <> DialogResult.OK Then Return Nothing
            Dim name = nameBox.Text.Trim()
            If name.Length = 0 Then Throw New IOException("Enter a preset name.")
            Return name
        End Using
    End Function
    Private Sub CreateCopy(copy As Boolean)
        Dim source = If(copy, draft.Copy(), New FilterPreset())
        Dim name = AskName(If(copy, draft.Name & " copy", "My filter"))
        If name Is Nothing OrElse Not LeaveDraft() Then Return
        source.Id = Guid.NewGuid().ToString("N") : source.Name = name : store.Save(source) : ReloadLibrary(source.Id)
    End Sub
    Private Sub RenamePreset()
        Dim name = AskName(draft.Name)
        If name Is Nothing Then Return
        draft.Name = name : MarkDirty() : SaveDraft() : ReloadLibrary(draft.Id)
    End Sub
    Private Sub DeletePreset()
        If {SelectedId(desktop), SelectedId(headset), settings.DesktopFilterId, settings.VrFilterId}.Contains(draft.Id) Then Throw New IOException("Select and save another preset for Desktop and VR before deleting this one.")
        If MessageBox.Show(Me, "Delete " & draft.Name & " and its adjustments?", "Delete preset", MessageBoxButtons.YesNo, MessageBoxIcon.Question) <> DialogResult.Yes Then Return
        store.Delete(draft.Id) : ReloadLibrary("original")
    End Sub
    Private Sub ImportPreset()
        If Not LeaveDraft() Then Return
        Using dialog As New OpenFileDialog With {.Filter = "DiRT2VR filter (*.json)|*.json", .CheckFileExists = True}
            If dialog.ShowDialog(Me) <> DialogResult.OK Then Return
            Dim preset = store.Import(dialog.FileName) : ReloadLibrary(preset.Id)
        End Using
    End Sub
    Private Sub ExportPreset()
        Using dialog As New SaveFileDialog With {.Filter = "DiRT2VR filter (*.json)|*.json", .FileName = "filter.d2vrfilter.json", .DefaultExt = "json", .AddExtension = True}
            If dialog.ShowDialog(Me) = DialogResult.OK Then FilterStore.Export(dialog.FileName, draft)
        End Using
    End Sub
    Public Sub Save(preferences As VrSettings)
        SaveDraft()
        Dim desktopId = SelectedId(desktop), vrId = SelectedId(headset)
        store.Load(desktopId) : store.Load(vrId)
        preferences.DesktopFilterId = desktopId : preferences.VrFilterId = vrId
    End Sub
    Public Sub ResetSelections()
        For Each box In {desktop, headset}
            box.SelectedItem = box.Items.Cast(Of FilterPreset)().First(Function(p) p.Id = "original")
        Next
    End Sub
    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then tips.Dispose()
        MyBase.Dispose(disposing)
    End Sub
End Class
