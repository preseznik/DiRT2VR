Imports System.Drawing

' VR render scale (ProJict ec0902fe): at 100% the game image has as many pixels as
' the connected headset's recommended eye image, like GRID2VR. Unlike there, the
' game image stays 4:3, because it is also the VR menu screen. The headset image
' always uses the recommended size. The native renderer accepts up to 4800 × 3600.
Public Module VrResolution
    Public Const MinimumScale As Integer = 50, MaximumScale As Integer = 300
    Public Const MaximumWidth As Integer = 4800, MaximumHeight As Integer = 3600
    Public Const MinimumWidth As Integer = 560, MinimumHeight As Integer = 420
    ' Before any headset was seen, 100% keeps the previous 1600 × 1200 baseline.
    Public ReadOnly FallbackEye As New Size(1600, 1200)
    Private Function Known(eye As Size) As Size
        Return If(eye.Width > 0 AndAlso eye.Height > 0, eye, FallbackEye)
    End Function
    Private Function FullWidth(eye As Size, fieldOfView As Integer) As Double
        eye = Known(eye)
        Return Math.Sqrt(CDbl(eye.Width) * eye.Height * 4 / 3) * fieldOfView / 100
    End Function
    Public Function Scene(eye As Size, scale As Integer, fieldOfView As Integer) As Size
        Dim width = FullWidth(eye, fieldOfView) * scale / 100
        If width > MaximumWidth Then Return New Size(MaximumWidth, MaximumHeight)
        Return New Size(Math.Max(MinimumWidth, CInt(Math.Round(width))), Math.Max(MinimumHeight, CInt(Math.Round(width * 3 / 4))))
    End Function
    ' The highest scale that fits the renderer's limit with this headset.
    Public Function Limit(eye As Size, fieldOfView As Integer) As Integer
        Return Math.Max(MinimumScale, Math.Min(MaximumScale, CInt(Math.Floor(MaximumWidth / FullWidth(eye, fieldOfView) * 100))))
    End Function
    ' The headset's recommended eye size from a preflight report ("eye0=2016x2240 …").
    Public Function EyeFromReport(report As String) As Size
        Dim match = System.Text.RegularExpressions.Regex.Match(If(report, ""), "(?m)^eye0=(\d{2,5})x(\d{2,5})\b")
        If Not match.Success Then Return Size.Empty
        Dim width = Integer.Parse(match.Groups(1).Value, Globalization.CultureInfo.InvariantCulture)
        Dim height = Integer.Parse(match.Groups(2).Value, Globalization.CultureInfo.InvariantCulture)
        Return If(width >= 100 AndAlso height >= 100 AndAlso width <= 16384 AndAlso height <= 16384, New Size(width, height), Size.Empty)
    End Function
End Module
