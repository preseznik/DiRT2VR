Imports System.Drawing
Imports System.Windows.Forms
Imports System.Xml.Linq

Public Module CarGrades
    Public ReadOnly Values As String() = {"rookie", "pro", "all-star"}
    Public ReadOnly Labels As String() = {"Rookie", "Pro", "All-Star"}
    Public Sub Validate(value As String)
        If Not Values.Contains(value) Then Throw New IOException("Choose Rookie, Pro or All-Star car grade.")
    End Sub
    Public Function GradeLabel(value As String) As String
        Validate(value) : Return Labels(Array.IndexOf(Values, value))
    End Function
    Public Function IsStock(code As String) As Boolean
        Return RaceCatalog.Current.Cars.Any(Function(c) c.Code = code)
    End Function
    Public Function Effective(settings As VrSettings) As String
        Validate(settings.CarGrade)
        Return If(settings.DirectMode AndAlso IsStock(settings.CarCode), settings.CarGrade, Nothing)
    End Function
    Public Sub Configure(start As ProcessStartInfo, settings As VrSettings)
        start.Environment.Remove("DIRT2VR_CAR_GRADE")
        Dim grade = Effective(settings)
        If grade IsNot Nothing Then start.Environment("DIRT2VR_CAR_GRADE") = (3 - Array.IndexOf(Values, grade)).ToString(Globalization.CultureInfo.InvariantCulture)
    End Sub
    Public Sub ValidateGrid(context As InstallContext, document As XDocument, player As String, grade As String)
        Validate(grade)
        If Not IsStock(player) Then Return
        Dim suffix = {"", "_B", "_A"}(Array.IndexOf(Values, grade))
        For Each code In document.Root.Element("track").Elements("car").Select(Function(c) CStr(c.Attribute("name"))).Distinct()
            If Not IsStock(code) Then Throw New IOException("Car grades require a stock-car grid.")
            Dim path = IO.Path.Combine(context.GameRoot, "cars", code, code & suffix & ".ctf")
            Files.NoLinks(path)
            If Not File.Exists(path) Then Throw New IOException(GradeLabel(grade) & " handling is missing for " & RaceCatalog.Current.Car(code).Label & ". Verify your game files.")
        Next
    End Sub
End Module

' Native radio buttons keep arrow-key navigation, focus and accessibility.
Public Class CarGradePicker
    Inherits FlowLayoutPanel
    Private ReadOnly segments As New List(Of RadioButton)
    Private ReadOnly specification As New Label With {.Text = "Custom specification", .AutoSize = True}
    Public Event ValueChanged As EventHandler
    Public Sub New()
        AutoSize = True : WrapContents = True : Margin = New Padding(0)
        AccessibleName = "Car grade"
        For i = 0 To CarGrades.Values.Length - 1
            Dim button As New RadioButton With {.Text = CarGrades.Labels(i), .Name = "Grade" & i, .Appearance = Appearance.Button,
                .AutoSize = True, .TextAlign = ContentAlignment.MiddleCenter, .FlatStyle = FlatStyle.Flat, .Margin = New Padding(0, 0, 2, 0)}
            segments.Add(button) : Controls.Add(button)
            AddHandler button.CheckedChanged, Sub()
                                                  UpdateColours()
                                                  If button.Checked Then RaiseEvent ValueChanged(Me, EventArgs.Empty)
                                              End Sub
        Next
        Controls.Add(specification) : specification.Visible = False
        segments(0).Checked = True
    End Sub
    <System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)>
    Public Property Value As String
        Get
            Return CarGrades.Values(segments.FindIndex(Function(b) b.Checked))
        End Get
        Set(value As String)
            CarGrades.Validate(value) : segments(Array.IndexOf(CarGrades.Values, value)).Checked = True
        End Set
    End Property
    Public Sub SetStock(available As Boolean)
        For Each button In segments : button.Enabled = available : Next
        specification.Visible = Not available
    End Sub
    Protected Overrides Sub OnBackColorChanged(e As EventArgs)
        MyBase.OnBackColorChanged(e) : UpdateColours()
    End Sub
    Protected Overrides Sub OnForeColorChanged(e As EventArgs)
        MyBase.OnForeColorChanged(e) : UpdateColours()
    End Sub
    Private Sub UpdateColours()
        If segments Is Nothing Then Return
        For Each button In segments
            button.BackColor = If(button.Checked, SystemColors.Highlight, BackColor)
            button.ForeColor = If(button.Checked, SystemColors.HighlightText, ForeColor)
        Next
    End Sub
End Class
