Public Class DetectionResult
    Public Property List As List(Of DetectionItem)
    Public Property ImageBase64 As String
    Public Property Mat As Object
    Public Property Stage As String
    Public Property IsFinal As Boolean
End Class

Public Class DetectionItem
    Public Property detectionNo As String
    Public Property taskPartName As String
    Public Property recognizedPartName As String
    Public Property recognizedPartCode As String
    Public Property collectImageUrl As String
    Public Property resultType As String
    Public Property confidence As Double
End Class
