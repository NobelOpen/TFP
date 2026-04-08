import sys, json
sys.stdout.write(json.dumps({
    "jsonrpc": "2.0",
    "id": 1,
    "method": "tools/call",
    "params": {
        "name": "submit_plan",
        "arguments": {
            "Goal": "创建Win截图任务",
            "Summary": "正在通过 MCP 插入一张测试功能的截图卡片...",
            "Plan": [{
                "TaskType": "WinScreenshot",
                "Name": "MCP 测试截图",
                "ActionDescription": "外部 MCP Server 自动创建"
            }]
        }
    }
}) + '\n')
sys.stdout.flush()

while True:
    try:
        line = sys.stdin.readline()
        if not line: break
        with open('mcp_out.txt', 'a') as f:
            f.write(line)
        break
    except EOFError:
        break
