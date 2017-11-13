$title = "Action"
$message = "Which action do you want to execute?"

$start  = New-Object System.Management.Automation.Host.ChoiceDescription "&Start"
$stop   = New-Object System.Management.Automation.Host.ChoiceDescription "S&top"
$abort  = New-Object System.Management.Automation.Host.ChoiceDescription "&Abort"
$pause  = New-Object System.Management.Automation.Host.ChoiceDescription "&Pause"
$resume = New-Object System.Management.Automation.Host.ChoiceDescription "&Resume"
$kill   = New-Object System.Management.Automation.Host.ChoiceDescription "&Kill"

$options = [System.Management.Automation.Host.ChoiceDescription[]]($start, $stop, $abort, $pause, $resume, $kill)

while($true)
{
    $result = $host.ui.PromptForChoice($title, $message, $options, 0)

    switch ($result)
    {
        0 {
            $timestamp = get-date -f yyyy-MM-dd@HH-mm-ss
            & "C:\Program Files\Redis\redis-cli.exe" publish cmd:capture:start $timestamp
        }
        1 { & "C:\Program Files\Redis\redis-cli.exe" publish cmd:capture:stop 0 }
        2 { & "C:\Program Files\Redis\redis-cli.exe" publish cmd:capture:abort 0 }
        3 { & "C:\Program Files\Redis\redis-cli.exe" publish cmd:capture:pause 0 }
        4 { & "C:\Program Files\Redis\redis-cli.exe" publish cmd:capture:resume 0 }
        5 { & "C:\Program Files\Redis\redis-cli.exe" publish cmd:kill 0 }
    }
}
