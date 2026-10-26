"""Five real rendered-window samples; 10s warmup + 30s frame intervals each."""
import json
from pathlib import Path
from desktop_test import QuestSimWindow, ROOT

out = ROOT / '.artifacts/benchmarks'
out.mkdir(parents=True, exist_ok=True)
for mode, camera in [('2d','overhead'), ('sprite3d','third'), ('sprite3d','first'), ('poly3d','third'), ('poly3d','first')]:
    name = mode + '-' + camera
    report = out / (name + '.json')
    report.unlink(missing_ok=True)
    window = QuestSimWindow('benchmark-' + name, '--mode=' + mode, '--camera=' + camera,
                            '--benchmark', '--report=' + str(report), scene='Lab.tscn')
    try:
        code = window.process.wait(timeout=55)
        assert code == 0, code
        log_text = Path(window.log.name).read_text()
        assert 'ERROR:' not in log_text and 'WARNING:' not in log_text, log_text
        data = json.loads(report.read_text())
        assert data['sample_frames'] > 30
        print(name, 'median/p95 ms', round(data['median_frame_ms'],3), round(data['p95_frame_ms'],3),
              'RSS MiB', round(data['rss_bytes']/1048576,1), flush=True)
    finally:
        window.close()
