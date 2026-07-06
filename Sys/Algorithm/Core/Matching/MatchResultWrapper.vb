''' <summary>Wraps a template match result with attempt count metadata.</summary>
Friend Class MatchResultWrapper
    Public Property Result As Draw_opencv.ResultPack
    Public Property MatchCount As Integer = 0
End Class
