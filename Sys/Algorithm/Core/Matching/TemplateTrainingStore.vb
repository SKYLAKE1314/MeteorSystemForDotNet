Imports OpenCvSharp
Imports System.Text.Json
Imports System.Linq

Public Class TemplateTrainingStore

    Private Shared ReadOnly _cacheLock As New Object()
    Private Shared ReadOnly _sampleMetaCache As New Dictionary(Of String, List(Of TrainingSampleMeta))(StringComparer.OrdinalIgnoreCase)
    Private Shared ReadOnly _sampleBytesCache As New Dictionary(Of String, Byte())(StringComparer.OrdinalIgnoreCase)

    Public Class TrainingTemplateParams
        Public Property MasterThreshold As Double = 0.8
        Public Property PyramidLevel As Integer = 2
        Public Property MatchMethod As Integer = 0
        Public Property MinArea As Integer = 50
        Public Property CannyLow As Integer = 80
        Public Property CannyHigh As Integer = 160
        Public Property AngleMin As Double = -60
        Public Property AngleMax As Double = 60
        Public Property AngleStep As Double = 3
        Public Property MaxSamples As Integer = 50
    End Class

    Public Class TrainingPoint
        Public Property X As Integer
        Public Property Y As Integer
    End Class

    Public Class TrainingSampleMeta
        Public Property FileName As String
        Public Property CreatedAt As Long
        Public Property LastMatchedAt As Long
        Public Property RoiX As Integer
        Public Property RoiY As Integer
        Public Property RoiW As Integer
        Public Property RoiH As Integer
        Public Property MasterThreshold As Double
        Public Property PyramidLevel As Integer
        Public Property MatchMethod As Integer
        Public Property MinArea As Integer
        Public Property CannyLow As Integer
        Public Property CannyHigh As Integer
        Public Property AngleMin As Double
        Public Property AngleMax As Double
        Public Property AngleStep As Double
        Public Property PolygonPoints As List(Of TrainingPoint) = New List(Of TrainingPoint)()
    End Class

    Private Class TrainingIndexFile
        Public Property Samples As List(Of TrainingSampleMeta) = New List(Of TrainingSampleMeta)()
    End Class

    Public Shared Function NormalizeGroupPath(path As String) As String
        If String.IsNullOrWhiteSpace(path) Then Return ""
        If Not IO.Directory.Exists(path) Then Return ""

        Dim name = IO.Path.GetFileName(path)
        If name IsNot Nothing AndAlso name.StartsWith("cam", StringComparison.OrdinalIgnoreCase) Then
            Dim parent = IO.Directory.GetParent(path)
            If parent IsNot Nothing AndAlso parent.Exists Then
                Return parent.FullName
            End If
        End If

        Return path
    End Function

    Public Shared Sub InvalidateCache(groupPath As String)
        Dim groupRoot = NormalizeGroupPath(groupPath)
        If String.IsNullOrWhiteSpace(groupRoot) Then Return
        SyncLock _cacheLock
            _sampleMetaCache.Remove(groupRoot)
            ' 移除該 group 下所有 sample 的圖片快取
            Dim keysToRemove = _sampleBytesCache.Keys.
                Where(Function(k) k.StartsWith(groupRoot, StringComparison.OrdinalIgnoreCase)).
                ToList()
            For Each k In keysToRemove
                _sampleBytesCache.Remove(k)
            Next
        End SyncLock
        Logger.Debug($"[TemplateTraining] 快取已清除: {groupRoot}")
    End Sub

    Public Shared Function GetTrainingSampleCount(groupPath As String) As Integer
        Dim groupRoot = NormalizeGroupPath(groupPath)
        If String.IsNullOrWhiteSpace(groupRoot) Then Return 0

        Dim idx = LoadIndex(groupRoot)
        Return idx.Samples.Count
    End Function

    Public Shared Function AddSamplePolygon(groupPath As String,
                                            source As Mat,
                                            polygon As List(Of Point),
                                            params As TrainingTemplateParams) As Integer

        If source Is Nothing Then Throw New ArgumentNullException(NameOf(source))
        If polygon Is Nothing OrElse polygon.Count < 3 Then Throw New ArgumentException("Polygon ROI invalid")

        Dim groupRoot = NormalizeGroupPath(groupPath)
        If String.IsNullOrWhiteSpace(groupRoot) Then Throw New IO.DirectoryNotFoundException("Template group not found")

        Cv2.SetUseOptimized(True)
        Cv2.SetNumThreads(Math.Max(1, Environment.ProcessorCount))

        Dim trainDir = IO.Path.Combine(groupRoot, "training")
        Dim sampleDir = IO.Path.Combine(trainDir, "samples")
        IO.Directory.CreateDirectory(sampleDir)

        Dim fileName = $"tpl_{DateTime.Now:yyyyMMdd_HHmmssfff}_{Guid.NewGuid().ToString("N").Substring(0, 8)}.png"
        Dim filePath = IO.Path.Combine(sampleDir, fileName)

        Dim safePoly = polygon.
            Select(Function(p) New Point(
                Math.Max(0, Math.Min(source.Width - 1, p.X)),
                Math.Max(0, Math.Min(source.Height - 1, p.Y)))).
            ToArray()

        Dim minX = safePoly.Min(Function(p) p.X)
        Dim maxX = safePoly.Max(Function(p) p.X)
        Dim minY = safePoly.Min(Function(p) p.Y)
        Dim maxY = safePoly.Max(Function(p) p.Y)

        Dim safeRoi As New Rect(minX, minY, maxX - minX + 1, maxY - minY + 1)
        If safeRoi.Width <= 0 OrElse safeRoi.Height <= 0 Then Throw New ArgumentException("Polygon bbox invalid")

        ' 直接裁剪 ROI 區域儲存，不縮放到母版大小
        ' 多邊形遮罩讓 ROI 外的像素填為深色背景
        Using mask As Mat = Mat.Zeros(source.Size(), MatType.CV_8UC1),
              solidFill As New Mat(source.Size(), source.Type(), New Scalar(32, 32, 32))
            Cv2.FillPoly(mask, {safePoly}, Scalar.White)
            source.CopyTo(solidFill, mask)
            Using crop As New Mat(solidFill, safeRoi)
                ' 軟化邊界以消除邊緣輪廓特徵
                Dim softened = SoftenCropBoundary(crop)
                Cv2.ImWrite(filePath, softened, {New ImageEncodingParam(ImwriteFlags.PngCompression, 3)})
                softened.Dispose()
            End Using
        End Using

        Dim now = DateTimeOffset.Now.ToUnixTimeMilliseconds()

        Dim newMeta As New TrainingSampleMeta With {
            .FileName = fileName,
            .CreatedAt = now,
            .LastMatchedAt = now,
            .RoiX = safeRoi.X,
            .RoiY = safeRoi.Y,
            .RoiW = safeRoi.Width,
            .RoiH = safeRoi.Height,
            .MasterThreshold = params.MasterThreshold,
            .PyramidLevel = params.PyramidLevel,
            .MatchMethod = params.MatchMethod,
            .MinArea = params.MinArea,
            .CannyLow = params.CannyLow,
            .CannyHigh = params.CannyHigh,
            .AngleMin = params.AngleMin,
            .AngleMax = params.AngleMax,
            .AngleStep = params.AngleStep,
            .PolygonPoints = safePoly.Select(Function(p) New TrainingPoint With {.X = p.X, .Y = p.Y}).ToList()
        }

        ' 每張訓練圖對應一個獨立的 JSON（與圖檔同名，副檔名換 .json）
        SaveSampleJson(sampleDir, newMeta)

        ' 同時更新 training_index.json（向下相容舊版讀取）
        Dim indexFile = LoadIndex(groupRoot)
        indexFile.Samples.Add(newMeta)
        PurgeOverflow(groupRoot, indexFile, Math.Max(1, params.MaxSamples))
        SaveIndex(groupRoot, indexFile)
        SaveParams(groupRoot, params)

        Return indexFile.Samples.Count
    End Function

    ''' <summary>
    ''' 軟化裁剪邊界以消除邊緣輪廓特徵
    ''' 防止邊界被模板匹配算法識別為目標特徵
    ''' </summary>
    Private Shared Function SoftenCropBoundary(crop As Mat) As Mat
        If crop Is Nothing OrElse crop.Empty() Then
            Return crop.Clone()
        End If

        Const PAD_SIZE As Integer = 4

        Dim result = crop.Clone()

        ' 創建遮罩用於邊界漸變
        Dim mask As New Mat(crop.Size(), MatType.CV_8UC1, Scalar.White)

        ' 線性漸變：邊界 -> 內部
        ' 左邊界漸變
        For x As Integer = 0 To Math.Min(PAD_SIZE - 1, crop.Width - 1)
            Dim alpha = CByte((CDbl(x) / CDbl(PAD_SIZE)) * 255)
            For y As Integer = 0 To crop.Height - 1
                mask.Set(Of Byte)(y, x, alpha)
            Next
        Next

        ' 右邊界漸變
        For x As Integer = Math.Max(0, crop.Width - PAD_SIZE) To crop.Width - 1
            Dim alpha = CByte((CDbl(crop.Width - 1 - x) / CDbl(PAD_SIZE)) * 255)
            For y As Integer = 0 To crop.Height - 1
                mask.Set(Of Byte)(y, x, alpha)
            Next
        Next

        ' 上邊界漸變
        For y As Integer = 0 To Math.Min(PAD_SIZE - 1, crop.Height - 1)
            Dim alpha = CByte((CDbl(y) / CDbl(PAD_SIZE)) * 255)
            For x As Integer = 0 To crop.Width - 1
                Dim existing = mask.At(Of Byte)(y, x)
                mask.Set(Of Byte)(y, x, Math.Min(existing, alpha))
            Next
        Next

        ' 下邊界漸變
        For y As Integer = Math.Max(0, crop.Height - PAD_SIZE) To crop.Height - 1
            Dim alpha = CByte((CDbl(crop.Height - 1 - y) / CDbl(PAD_SIZE)) * 255)
            For x As Integer = 0 To crop.Width - 1
                Dim existing = mask.At(Of Byte)(y, x)
                mask.Set(Of Byte)(y, x, Math.Min(existing, alpha))
            Next
        Next

        ' 將邊界區域與深色背景混合
        Dim bg As New Mat(crop.Size(), crop.Type(), New Scalar(32, 32, 32))

        ' 使用遮罩進行邊界軟化（邊界逐漸過渡到背景色）
        For y As Integer = 0 To crop.Height - 1
            For x As Integer = 0 To crop.Width - 1
                Dim maskVal = CDbl(mask.At(Of Byte)(y, x)) / 255.0
                If maskVal < 1.0 Then
                    Dim pixel = crop.At(Of Vec3b)(y, x)
                    Dim bgPixel = bg.At(Of Vec3b)(y, x)

                    ' 線性插值
                    pixel.Item0 = CByte(pixel.Item0 * maskVal + bgPixel.Item0 * (1 - maskVal))
                    pixel.Item1 = CByte(pixel.Item1 * maskVal + bgPixel.Item1 * (1 - maskVal))
                    pixel.Item2 = CByte(pixel.Item2 * maskVal + bgPixel.Item2 * (1 - maskVal))

                    result.Set(Of Vec3b)(y, x, pixel)
                End If
            Next
        Next

        ' 輕微高斯模糊以進一步軟化邊界
        Cv2.GaussianBlur(result, result, New Size(3, 3), 0.5)

        mask.Dispose()
        bg.Dispose()

        Return result
    End Function

    Private Shared Function ResolveTemplateSize(groupRoot As String, fallback As Size) As Size
        Try
            Dim candidatePaths As New List(Of String)()
            For Each cam In IO.Directory.GetDirectories(groupRoot, "cam*")
                candidatePaths.Add(IO.Path.Combine(cam, "template.png"))
            Next
            candidatePaths.Add(IO.Path.Combine(groupRoot, "template.png"))

            For Each p In candidatePaths
                If Not IO.File.Exists(p) Then Continue For
                Using mat = Cv2.ImRead(p, ImreadModes.Unchanged)
                    If mat IsNot Nothing AndAlso Not mat.Empty() Then
                        Return mat.Size()
                    End If
                End Using
            Next
        Catch ex As Exception
            Logger.Warn("[TemplateTraining] resolve template size failed: " & ex.Message)
        End Try

        Return fallback
    End Function

    Public Shared Sub TouchLatestMatched(groupPath As String)
        Dim groupRoot = NormalizeGroupPath(groupPath)
        If String.IsNullOrWhiteSpace(groupRoot) Then Return

        Dim idx = LoadIndex(groupRoot)
        If idx.Samples.Count = 0 Then Return

        Dim latest = idx.Samples.OrderByDescending(Function(x) x.LastMatchedAt).ThenByDescending(Function(x) x.CreatedAt).First()
        latest.LastMatchedAt = DateTimeOffset.Now.ToUnixTimeMilliseconds()
        SaveIndex(groupRoot, idx)
    End Sub

    Public Shared Function GetTrainingSamples(groupPath As String) As List(Of TrainingSampleMeta)
        Dim groupRoot = NormalizeGroupPath(groupPath)
        If String.IsNullOrWhiteSpace(groupRoot) Then Return New List(Of TrainingSampleMeta)()

        SyncLock _cacheLock
            If _sampleMetaCache.ContainsKey(groupRoot) Then
                Return _sampleMetaCache(groupRoot).
                    Select(Function(x) CloneMeta(x)).
                    OrderByDescending(Function(x) x.LastMatchedAt).
                    ThenByDescending(Function(x) x.CreatedAt).
                    ToList()
            End If
        End SyncLock

        ' ── 主要：讀取 samples/ 目錄下每張圖對應的獨立 JSON ──────────────
        Dim sampleDir = IO.Path.Combine(groupRoot, "training", "samples")
        Dim merged As New Dictionary(Of String, TrainingSampleMeta)(StringComparer.OrdinalIgnoreCase)

        If IO.Directory.Exists(sampleDir) Then
            For Each jsonFile In IO.Directory.GetFiles(sampleDir, "*.json")
                Try
                    Dim json = IO.File.ReadAllText(jsonFile)
                    Dim meta = JsonSerializer.Deserialize(Of TrainingSampleMeta)(json)
                    If meta IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(meta.FileName) Then
                        merged(meta.FileName) = meta
                    End If
                Catch ex As Exception
                    Logger.Warn($"[TemplateTraining] 讀取獨立 JSON 失敗: {jsonFile} — {ex.Message}")
                End Try
            Next
        End If

        ' ── 向下相容：從 training_index.json 補充缺少獨立 JSON 的舊樣本 ──
        Dim idx = LoadIndex(groupRoot)
        For Each s In idx.Samples
            If Not merged.ContainsKey(s.FileName) Then
                merged(s.FileName) = s
            End If
        Next

        ' 過濾掉已不存在對應圖檔的項目
        Dim validSamples = merged.Values.
            Where(Function(m) IO.File.Exists(IO.Path.Combine(sampleDir, m.FileName))).
            OrderByDescending(Function(x) x.LastMatchedAt).
            ThenByDescending(Function(x) x.CreatedAt).
            ToList()

        SyncLock _cacheLock
            _sampleMetaCache(groupRoot) = validSamples.Select(Function(x) CloneMeta(x)).ToList()
        End SyncLock

        Return validSamples
    End Function

    Public Shared Function GetTrainingSampleMeta(groupPath As String, fileName As String) As TrainingSampleMeta
        If String.IsNullOrWhiteSpace(fileName) Then Return Nothing

        Dim samples = GetTrainingSamples(groupPath)
        Dim meta = samples.FirstOrDefault(Function(x) String.Equals(x.FileName, fileName, StringComparison.OrdinalIgnoreCase))
        If meta Is Nothing Then Return Nothing
        Return CloneMeta(meta)
    End Function

    Public Shared Function LoadTrainingSampleImage(groupPath As String, fileName As String) As Mat
        Dim groupRoot = NormalizeGroupPath(groupPath)
        If String.IsNullOrWhiteSpace(groupRoot) OrElse String.IsNullOrWhiteSpace(fileName) Then Return Nothing

        Dim cacheKey = IO.Path.Combine(groupRoot, "training", "samples", fileName)

        SyncLock _cacheLock
            If _sampleBytesCache.ContainsKey(cacheKey) Then
                Return Cv2.ImDecode(_sampleBytesCache(cacheKey), ImreadModes.Color)
            End If
        End SyncLock

        If Not IO.File.Exists(cacheKey) Then Return Nothing

        Dim bytes = IO.File.ReadAllBytes(cacheKey)
        SyncLock _cacheLock
            _sampleBytesCache(cacheKey) = bytes
        End SyncLock
        Return Cv2.ImDecode(bytes, ImreadModes.Color)
    End Function

    Public Shared Sub WarmupAll()
        Dim root = IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Templates")
        If Not IO.Directory.Exists(root) Then Return

        SyncLock _cacheLock
            _sampleMetaCache.Clear()
            _sampleBytesCache.Clear()
        End SyncLock

        For Each groupRoot In IO.Directory.GetDirectories(root)
            Dim normalized = NormalizeGroupPath(groupRoot)
            If String.IsNullOrWhiteSpace(normalized) Then Continue For

            ' 使用 GetTrainingSamples 讀取（自動合併獨立 JSON + index.json 回退）
            Dim samples = GetTrainingSamples(normalized)

            Dim sampleDir = IO.Path.Combine(normalized, "training", "samples")
            If Not IO.Directory.Exists(sampleDir) Then Continue For

            For Each sample In samples
                Dim filePath = IO.Path.Combine(sampleDir, sample.FileName)
                If Not IO.File.Exists(filePath) Then Continue For
                Try
                    Dim bytes = IO.File.ReadAllBytes(filePath)
                    SyncLock _cacheLock
                        _sampleBytesCache(filePath) = bytes
                    End SyncLock
                Catch ex As Exception
                    Logger.Warn("[TemplateTraining] warmup sample failed: " & ex.Message)
                End Try
            Next
        Next
    End Sub

    Public Shared Function DeleteTrainingSample(groupPath As String, fileName As String) As Boolean
        Dim groupRoot = NormalizeGroupPath(groupPath)
        If String.IsNullOrWhiteSpace(groupRoot) OrElse String.IsNullOrWhiteSpace(fileName) Then Return False

        Dim sampleDir = IO.Path.Combine(groupRoot, "training", "samples")

        ' 刪除圖檔
        Dim filePath = IO.Path.Combine(sampleDir, fileName)
        If IO.File.Exists(filePath) Then IO.File.Delete(filePath)

        ' 刪除對應的獨立 JSON
        Dim jsonPath = IO.Path.Combine(sampleDir, IO.Path.GetFileNameWithoutExtension(fileName) & ".json")
        If IO.File.Exists(jsonPath) Then IO.File.Delete(jsonPath)

        ' 從 training_index.json 中也移除（向下相容）
        Dim idx = LoadIndex(groupRoot)
        Dim item = idx.Samples.FirstOrDefault(Function(x) String.Equals(x.FileName, fileName, StringComparison.OrdinalIgnoreCase))
        If item IsNot Nothing Then
            idx.Samples.Remove(item)
            SaveIndex(groupRoot, idx)
        End If

        SyncLock _cacheLock
            Dim cacheKey = IO.Path.Combine(sampleDir, fileName)
            _sampleBytesCache.Remove(cacheKey)
            _sampleMetaCache.Remove(groupRoot)
        End SyncLock

        Return True
    End Function

    Private Shared Sub PurgeOverflow(groupRoot As String,
                                     idx As TrainingIndexFile,
                                     maxSamples As Integer)
        If idx.Samples.Count <= maxSamples Then Return

        Dim keep = idx.Samples.
            OrderByDescending(Function(x) x.LastMatchedAt).
            ThenByDescending(Function(x) x.CreatedAt).
            Take(maxSamples).
            ToList()

        Dim keepSet = New HashSet(Of String)(keep.Select(Function(x) x.FileName), StringComparer.OrdinalIgnoreCase)

        Dim sampleDir = IO.Path.Combine(groupRoot, "training", "samples")
        For Each s In idx.Samples
            If keepSet.Contains(s.FileName) Then Continue For
            ' 刪除圖檔
            Dim fp = IO.Path.Combine(sampleDir, s.FileName)
            If IO.File.Exists(fp) Then
                Try
                    IO.File.Delete(fp)
                Catch ex As Exception
                    Logger.Warn("[TemplateTraining] delete sample failed: " & ex.Message)
                End Try
            End If
            ' 刪除對應的獨立 JSON
            Dim jp = IO.Path.Combine(sampleDir, IO.Path.GetFileNameWithoutExtension(s.FileName) & ".json")
            If IO.File.Exists(jp) Then
                Try
                    IO.File.Delete(jp)
                Catch : End Try
            End If
        Next

        idx.Samples = keep
    End Sub

    Private Shared Function LoadIndex(groupRoot As String) As TrainingIndexFile
        Dim idxPath = IO.Path.Combine(groupRoot, "training", "training_index.json")
        If Not IO.File.Exists(idxPath) Then Return New TrainingIndexFile()

        Try
            Dim json = IO.File.ReadAllText(idxPath)
            Dim data = JsonSerializer.Deserialize(Of TrainingIndexFile)(json)
            If data Is Nothing Then Return New TrainingIndexFile()
            If data.Samples Is Nothing Then data.Samples = New List(Of TrainingSampleMeta)()
            Return data
        Catch ex As Exception
            Logger.Warn("[TemplateTraining] load index failed: " & ex.Message)
            Return New TrainingIndexFile()
        End Try
    End Function

    Private Shared Sub SaveIndex(groupRoot As String, idx As TrainingIndexFile)
        Dim dir = IO.Path.Combine(groupRoot, "training")
        IO.Directory.CreateDirectory(dir)
        Dim idxPath = IO.Path.Combine(dir, "training_index.json")
        Dim json = JsonSerializer.Serialize(idx, New JsonSerializerOptions With {.WriteIndented = True})
        IO.File.WriteAllText(idxPath, json)

        SyncLock _cacheLock
            _sampleMetaCache(groupRoot) = idx.Samples.Select(Function(x) CloneMeta(x)).ToList()
        End SyncLock
    End Sub

    ''' <summary>
    ''' 將單個訓練樣本的 meta 儲存為與圖檔同名的獨立 JSON
    ''' （例如 tpl_20250101_001.png → tpl_20250101_001.json）
    ''' </summary>
    Private Shared Sub SaveSampleJson(sampleDir As String, meta As TrainingSampleMeta)
        If meta Is Nothing OrElse String.IsNullOrWhiteSpace(meta.FileName) Then Return
        Try
            IO.Directory.CreateDirectory(sampleDir)
            Dim jsonPath = IO.Path.Combine(sampleDir, IO.Path.GetFileNameWithoutExtension(meta.FileName) & ".json")
            Dim json = JsonSerializer.Serialize(meta, New JsonSerializerOptions With {.WriteIndented = True})
            IO.File.WriteAllText(jsonPath, json)
        Catch ex As Exception
            Logger.Warn($"[TemplateTraining] 儲存獨立 JSON 失敗: {meta.FileName} — {ex.Message}")
        End Try
    End Sub

    Private Shared Sub SaveParams(groupRoot As String, params As TrainingTemplateParams)
        Dim dir = IO.Path.Combine(groupRoot, "training")
        IO.Directory.CreateDirectory(dir)
        Dim cfgPath = IO.Path.Combine(dir, "training_params.json")
        Dim json = JsonSerializer.Serialize(params, New JsonSerializerOptions With {.WriteIndented = True})
        IO.File.WriteAllText(cfgPath, json)
    End Sub

    Private Shared Function CloneMeta(source As TrainingSampleMeta) As TrainingSampleMeta
        If source Is Nothing Then Return Nothing

        Return New TrainingSampleMeta With {
            .FileName = source.FileName,
            .CreatedAt = source.CreatedAt,
            .LastMatchedAt = source.LastMatchedAt,
            .RoiX = source.RoiX,
            .RoiY = source.RoiY,
            .RoiW = source.RoiW,
            .RoiH = source.RoiH,
            .MasterThreshold = source.MasterThreshold,
            .PyramidLevel = source.PyramidLevel,
            .MatchMethod = source.MatchMethod,
            .MinArea = source.MinArea,
            .CannyLow = source.CannyLow,
            .CannyHigh = source.CannyHigh,
            .AngleMin = source.AngleMin,
            .AngleMax = source.AngleMax,
            .AngleStep = source.AngleStep,
            .PolygonPoints = source.PolygonPoints.Select(Function(p) New TrainingPoint With {.X = p.X, .Y = p.Y}).ToList()
        }
    End Function

End Class
