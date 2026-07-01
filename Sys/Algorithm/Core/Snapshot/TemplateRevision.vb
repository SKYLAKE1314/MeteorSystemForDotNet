Public Class TemplateRevision

    Public Property RevisionId As String
    Public Property Timestamp As Long
    Public Property TemplatePath As String

    Public Property Threshold As Double
    Public Property MatchMethod As Integer

    Public Property RoiX As Integer
    Public Property RoiY As Integer
    Public Property RoiW As Integer
    Public Property RoiH As Integer

    Public Property OcrExpectedText As String
    Public Property BarcodeExpectedText As String

    Public Property Comment As String
    Public Property Author As String

End Class
