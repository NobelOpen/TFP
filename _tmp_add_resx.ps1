$resxPath = "c:\Users\31640\source\repos\TaskFlow\Resources\Strings.resx"
$content = [System.IO.File]::ReadAllText($resxPath)

$newEntries = @"
  <data name="TaskType_ImgOnnxDetect" xml:space="preserve">
    <value>ONNX Detect</value>
  </data>
  <data name="Main_VisionModels" xml:space="preserve">
    <value>Vision Models</value>
  </data>
"@

$marker = "    <value>Image Resize</value>`r`n  </data>"
$replacement = "    <value>Image Resize</value>`r`n  </data>`r`n" + $newEntries

$content = $content.Replace($marker, $replacement)
[System.IO.File]::WriteAllText($resxPath, $content)
Write-Host "Done: Strings.resx updated"
