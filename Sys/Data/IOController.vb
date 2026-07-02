Imports System
Imports System.IO
Imports System.Media
Imports System.Threading
Imports System.Threading.Tasks
Imports MetroSystemForDotNet.IoBoard

Public Class IOController
    Implements IDisposable

#Region "Fields"

    Private Const HardwareCooldownMilliseconds As Integer = 500
    Private Const OkVoiceFileName As String = "Correct.wav"
    Private Const NgVoiceFileName As String = "Error.wav"

    Private ReadOnly _ip As String
    Private ReadOnly _port As Integer
    Private ReadOnly _unitId As Byte
    Private ReadOnly _coilBase As UShort
    Private ReadOnly _mode As IoBoardMode
    Private ReadOnly _hardwareEnabled As Boolean
    Private ReadOnly _actionLock As New SemaphoreSlim(1, 1)
    Private ReadOnly _stateLock As New Object()
    Private ReadOnly _log As Action(Of String)

    Private _buzzer As ModbusBuzzer
    Private _diScanner As DIScanf
    Public Event ButtonChanged As Action(Of Boolean)
    Private _lastHardwareActionUtc As DateTimeOffset = DateTimeOffset.MinValue

#End Region

#Region "Ctor"

    Public Sub New(ip As String,
                   port As Integer,
                   unitId As Byte,
                   coilBase As UShort,
                   mode As IoBoardMode,
                   log As Action(Of String))

        _ip = ip
        _port = port
        _unitId = unitId
        _coilBase = coilBase
        _mode = mode
        _hardwareEnabled = IoBoardModeHelper.IsHardwareEnabled(mode)

        If log Is Nothing Then
            _log = Sub(message As String)
                   End Sub
        Else
            _log = log
        End If

    End Sub

#End Region

#Region "Init"

    Public Async Function InitializeAsync() As Task

        If Not _hardwareEnabled Then
            _log("IO Mode = NONE，僅保留語音播報")
            Return
        End If

        Try
            _buzzer = New ModbusBuzzer(_ip, _port, _unitId, _coilBase, _mode)

            _buzzer.Logger = Sub(message)
                                 _log("[Buzzer] " & message)
                             End Sub

            Await Task.Run(Sub() _buzzer.Connect()).ConfigureAwait(False)

            _log("IO卡初始化完成")

        Catch ex As Exception
            _log("IO初始化失敗: " & ex.Message)
            SafeDispose()
        End Try

    End Function

#End Region

#Region "Public API"

    Public Sub Trigger(label As String)
        If String.IsNullOrWhiteSpace(label) Then Return

        label = label.Trim()

        If label.IndexOf("OK", StringComparison.OrdinalIgnoreCase) >= 0 Then
            HandleOK()
        ElseIf label.IndexOf("NG", StringComparison.OrdinalIgnoreCase) >= 0 Then
            HandleNG()
        End If
    End Sub

    Public Sub TriggerByScore(score As Double, threshold As Double)

        _log($"Score={score:F3}, Threshold={threshold:F3}")

        If score >= threshold Then
            HandleOK()
        Else
            HandleNG()
        End If

    End Sub

#End Region

#Region "OK / NG"

    Public Sub HandleOK()
        QueueResult("OK", OkVoiceFileName, AddressOf ExecuteOkHardwareAsync)
    End Sub

    Public Sub HandleNG()
        QueueResult("NG", NgVoiceFileName, AddressOf ExecuteNgHardwareAsync)
    End Sub

    Private Sub QueueResult(resultName As String,
                            voiceFileName As String,
                            hardwareAction As Func(Of Task))

        If Not _hardwareEnabled Then
            Task.Run(Sub() PlayVoice(voiceFileName))
            _log($"收到{resultName}（語音播報）")
            Return
        End If

        If Not TryReserveHardwareAction() Then Return

        Task.Run(Async Function() As Task

                     Dim lockAcquired As Boolean = False

                     Try
                         Await _actionLock.WaitAsync().ConfigureAwait(False)
                         lockAcquired = True

                         PlayVoice(voiceFileName)

                         If hardwareAction IsNot Nothing Then
                             Await hardwareAction().ConfigureAwait(False)
                         End If

                     Catch ex As Exception
                         _log($"{resultName}動作失敗: {ex.Message}")

                     Finally
                         If lockAcquired Then
                             _actionLock.Release()
                         End If
                     End Try

                 End Function)

    End Sub

    Private Function TryReserveHardwareAction() As Boolean

        SyncLock _stateLock

            Dim nowUtc = DateTimeOffset.UtcNow
            Dim cooldown = TimeSpan.FromMilliseconds(HardwareCooldownMilliseconds)

            If nowUtc - _lastHardwareActionUtc < cooldown Then
                Return False
            End If

            _lastHardwareActionUtc = nowUtc
            Return True

        End SyncLock

    End Function

    Private Function ExecuteOkHardwareAsync() As Task

        SetTowerLight(False, False, True) ' R=off, Y=off, G=on

        _buzzer?.SetCoil(0, True)   ' 綠燈
        _buzzer?.SetCoil(1, False)  ' 紅燈
        _buzzer?.SetCoil(3, False)  ' 黃燈 / buzzer off

        _log("收到OK")

        Return Task.CompletedTask

    End Function

    Private Async Function ExecuteNgHardwareAsync() As Task

        SetTowerLight(False, True, False)

        _buzzer?.SetCoil(3, True)

        Await Task.Delay(2000).ConfigureAwait(False)

        _buzzer?.SetCoil(3, False)
        _buzzer?.SetCoil(0, False)
        _buzzer?.SetCoil(1, False)

        _log("收到NG")

    End Function

#End Region

#Region "Tower Light"

    Public Sub SetTowerLight(red As Boolean, yellow As Boolean, green As Boolean)
        If Not _hardwareEnabled Then Return

        Try
            If _buzzer Is Nothing Then Return

            _log($"燈號 R={red} Y={yellow} G={green}")

            _buzzer.SetDO(1, green)
            _buzzer.SetDO(2, red)
            _buzzer.SetDO(3, yellow)

        Catch ex As Exception
            _log("燈號失敗: " & ex.Message)
        End Try

    End Sub

#End Region

#Region "Voice"

    Private Sub PlayVoice(fileName As String)

        Try
            If String.IsNullOrWhiteSpace(fileName) Then Return

            Dim filePath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "Voice",
                fileName)

            If Not File.Exists(filePath) Then
                _log("語音不存在: " & filePath)
                Return
            End If

            Using player As New SoundPlayer(filePath)
                player.PlaySync()
            End Using

        Catch ex As Exception
            _log("語音錯誤: " & ex.Message)
        End Try

    End Sub

    Public Sub PlayCustomVoice(fileName As String)
        If String.IsNullOrWhiteSpace(fileName) Then Return
        Task.Run(Sub() PlayVoice(fileName))
    End Sub

    ' -----------------------------
    ' DI Scanner 控制
    ' -----------------------------
    Public Sub StartDIListener(diIndex As Byte)

        If Not _hardwareEnabled Then
            _log("IO 已停用，無法啟用 DI 監聽")
            Return
        End If

        If _buzzer Is Nothing Then
            _log("IO 卡未就緒，無法啟用 DI 監聽")
            Return
        End If

        Try
            SafeStopDIListener()

            _diScanner = New DIScanf(_buzzer, diIndex)

            AddHandler _diScanner.OnButtonChanged, Sub(state)
                                                       Try
                                                           RaiseEvent ButtonChanged(state)
                                                       Catch ex As Exception
                                                           _log("DI 事件處理失敗: " & ex.Message)
                                                       End Try
                                                   End Sub

            _diScanner.Start()

            _log($"DI 監聽已啟用 (Index={diIndex})")

        Catch ex As Exception
            _log("啟用 DI 監聽失敗: " & ex.Message)
        End Try

    End Sub

    Public Sub StopDIListener()
        SafeStopDIListener()
    End Sub

    Private Sub SafeStopDIListener()
        Try
            If _diScanner IsNot Nothing Then
                _diScanner.Dispose()
            End If
        Catch
        End Try

        _diScanner = Nothing
    End Sub

#End Region

#Region "Dispose"

    Public Sub Dispose() Implements IDisposable.Dispose
        SafeDispose()
    End Sub

    Private Sub SafeDispose()

        Try
            _buzzer?.Dispose()
        Catch
        End Try

        Try
            _diScanner?.Dispose()
        Catch
        End Try

    End Sub

#End Region

End Class
