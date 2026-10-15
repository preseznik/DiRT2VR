Imports System.Windows.Forms
Imports System.ComponentModel

Public Class RoadSheenControl
    Inherits VerticalStack
    Private ReadOnly trackDefault As CheckBox
    Private ReadOnly strength As ValueSlider
    Private ReadOnly support As New Label With {.AutoSize = True, .UseMnemonic = False}
    Private supported As Boolean
    Public Sub New(prefix As String)
        Name = prefix & "RoadSheen" : Margin = New Padding(0)
        trackDefault = New CheckBox With {.Text = "Use track default", .Name = prefix & "RoadSheenDefault", .AutoSize = True, .Checked = True}
        strength = New ValueSlider(prefix & "RoadSheenStrength", 0, 100, 25, "%") With {.Enabled = False}
        support.Name = prefix & "RoadSheenSupport"
        Controls.AddRange({trackDefault, strength, support})
        AddHandler trackDefault.CheckedChanged, Sub() strength.Enabled = supported AndAlso Not trackDefault.Checked
        SetTrack(Nothing)
    End Sub
    <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
    Public Property UseTrackDefault As Boolean
        Get
            Return trackDefault.Checked
        End Get
        Set(value As Boolean)
            trackDefault.Checked = value
        End Set
    End Property
    <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
    Public Property Percent As Integer
        Get
            Return strength.Value
        End Get
        Set(value As Integer)
            strength.Value = value
        End Set
    End Property
    Public Sub SetTrack(layout As String)
        supported = Nordschleife.RoadSheen.Supports(layout)
        strength.Enabled = supported AndAlso Not trackDefault.Checked
        support.Text = If(supported, "Selected track supports road sheen. Applies next launch.",
            If(layout Is Nothing, "Select a track or replay. Nordschleife and Misty Loch are supported.", "Selected track has no road sheen control. Nordschleife and Misty Loch are supported."))
    End Sub
End Class
