import time, sys, subprocess

process = subprocess.Popen(
    [r'C:\Users\31640\AppData\Local\TaskFlow\Textractor\Textractor\x64\TextractorCLI.exe'],
    stdin=subprocess.PIPE,
    stdout=subprocess.PIPE,
    stderr=subprocess.PIPE
)

process.stdin.write(b'HB0@0 -P12345\n')
process.stdin.flush()

time.sleep(2)
process.kill()
