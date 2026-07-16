import json
import subprocess
import re
import os
from pathlib import Path

repository = os.environ["REPOSITORY"]
STAGES = ("development", "test", "production")
ENVIRONMENT_PATTERN = re.compile(
    r"^(.*?)-(development|test|production)$",
    re.IGNORECASE,
)

result = subprocess.run(
    [
        "gh",
        "api",
        f"repos/{repository}/deployments?per_page=200",
    ],
    check=True,
    capture_output=True,
    text=True,
)

deployments = json.loads(result.stdout)

deployments.sort(
    key=lambda x: x.get("created_at", ""),
    reverse=True
)

matrix = {}

for deployment in deployments:
    env_name = deployment.get("environment", "")

    match = ENVIRONMENT_PATTERN.match(env_name)

    if not match:
        continue

    app = match.group(1).upper()
    stage = match.group(2).lower()
    
    raw_ref = deployment.get("ref", "unknown")
    ref = raw_ref.replace("|", "-").replace("`", "")

    if app not in matrix:
        matrix[app] = {s: "-" for s in STAGES}

    if matrix[app][stage] == "-":
        matrix[app][stage] = f"`{ref}`"

markdown = [
    "| Application | 🛠️ Development | 🧪 Test | 🚀 Production |",
    "| :--- | :---: | :---: | :---: |"
]

for app in sorted(matrix.keys()):
    markdown.append(
        f"| **{app}** | {matrix[app]['development']} | {matrix[app]['test']} | {matrix[app]['production']} |"
    )

if not matrix:
    markdown.append("| _No deployments found_ | - | - | - |")

README_PATH = Path("README.md")

start = "<!-- DEPLOYMENT_MATRIX_START -->"
end = "<!-- DEPLOYMENT_MATRIX_END -->"

matrix_block = "\n".join(markdown)
new_block = f"{start}\n\n{matrix_block}\n\n{end}"

readme = README_PATH.read_text(encoding="utf-8")

existing_block = None

if start in readme and end in readme:
    existing_block = re.search(
        f"{start}.*?{end}",
        readme,
        flags=re.DOTALL
    ).group(0)

    if existing_block.strip() == new_block.strip():
        print("No changes to deployment matrix. Skipping README update.")
        raise SystemExit(0)

    updated = re.sub(
        f"{start}.*?{end}",
        new_block,
        readme,
        flags=re.DOTALL
    )
else:
    updated = readme + "\n\n" + new_block

README_PATH.write_text(updated, encoding="utf-8")