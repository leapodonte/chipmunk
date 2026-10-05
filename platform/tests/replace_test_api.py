"""仅更新带本测试标签的临时 API 容器，保留数据库与本机端口供浏览器验证。"""
import argparse
import json
from pathlib import Path
import re
import subprocess


def docker(*args):
    return subprocess.run(["docker", *args], check=True, capture_output=True, text=True).stdout.strip()


parser = argparse.ArgumentParser()
parser.add_argument("container")
parser.add_argument("--image", default="chipmunk-platform:dso-full-candidate")
args = parser.parse_args()
details = json.loads(docker("inspect", args.container))[0]
run = details["Config"]["Labels"].get("smilelab.test", "")
if not re.fullmatch(r"dso-test-[0-9a-f]{10}", run) or args.container != run + "-api" or not args.image.startswith("chipmunk-platform:dso-"):
    raise SystemExit("Refusing to modify a container outside the isolated DSO test environment")
files = list(Path("/tmp").glob(run + "-*/test.env"))
if len(files) != 1:
    raise SystemExit("Expected exactly one private temporary test environment")
env = files[0].resolve()
if not env.is_relative_to(Path("/tmp")):
    raise SystemExit("Test environment must remain under /tmp")
port = docker("port", args.container, "8080/tcp").rsplit(":", 1)[1]
docker("rm", "-f", "-v", args.container)
docker("run", "-d", "--name", args.container, "--network", run, "--label", "smilelab.test=" + run,
       "--cpus", "1", "--memory", "512m", "-p", "127.0.0.1:" + port + ":8080", "--env-file", str(env), args.image)
print("Temporary test API replaced; port and database preserved")
