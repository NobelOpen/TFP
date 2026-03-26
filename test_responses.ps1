$ErrorActionPreference = 'Stop'
$imgPath = "C:\Users\31640\Pictures\TaskFlow_20260315_152225.png"
$imgBytes = [System.IO.File]::ReadAllBytes($imgPath)
$base64 = [Convert]::ToBase64String($imgBytes)

$body = @{
    model = "gpt-5.4"
    stream = $true
    system = "You are TaskFlow AI, an intelligent desktop automation agent. Do exactly what the user asks using tools."
    input = @(
        @{
            role = "user"
            content = @(
                @{ type = "input_text"; text = "请查看当前桌面有什么，并告诉我你的发现。" },
                @{ type = "input_image"; image_url = "data:image/png;base64,$base64" }
            )
        }
    )
    tools = @(
        @{
            type = "function"
            name = "submit_plan"
            description = "提交任务流程卡片编排方案。这是整个自动化机器人的核心！"
            parameters = @{
                type = "object"
                properties = @{
                    plan = @{
                        type = "array"
                        description = "新建卡片"
                        items = @{ type = "object" }
                    }
                    done = @{ type = "boolean" }
                }
            }
        }
    )
}

$jsonBody = ConvertTo-Json -Depth 10 $body
Write-Host "JSON Size: ($jsonBody.Length) bytes"

$response = Invoke-RestMethod -Uri "https://api.312800.xyz/v1/responses" -Method Post -Headers @{
    "Authorization" = "Bearer tvV7m8Nk0OOD47kUE9YoQERIuZoAipgtaV6xhhkmbJuTO5OA"
    "Content-Type" = "application/json"
} -Body $jsonBody

$response
