Public Enum IoBoardMode
    IO
    PLC
    NONE
End Enum

Public Module IoBoardModeHelper

    Public Function Parse(value As String,
                          Optional fallback As IoBoardMode = IoBoardMode.NONE) As IoBoardMode

        If String.IsNullOrWhiteSpace(value) Then
            Return fallback
        End If

        Dim mode As IoBoardMode = fallback

        If [Enum].TryParse(value, True, mode) Then
            Return mode
        End If

        Return fallback

    End Function

    Public Function IsHardwareEnabled(mode As IoBoardMode) As Boolean
        Return mode <> IoBoardMode.NONE
    End Function

End Module
