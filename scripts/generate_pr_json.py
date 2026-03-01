#!/usr/bin/env python3
"""
Generates a normalized PR JSON file from the GitHub API.
Usage: GITHUB_TOKEN=<token> python3 scripts/generate_pr_json.py <owner> <repo> <pr_number>
Example: GITHUB_TOKEN=ghp_xxx python3 scripts/generate_pr_json.py mindsdb mindsdb 9876
"""
import json
import os
import sys
import urllib.request
import urllib.error
from datetime import datetime


def github_get(url: str, token: str) -> dict:
    req = urllib.request.Request(
        url,
        headers={
            "Authorization": f"Bearer {token}",
            "Accept": "application/vnd.github+json",
            "X-GitHub-Api-Version": "2022-11-28",
            "User-Agent": "PullRequestAnalyzer",
        },
    )
    with urllib.request.urlopen(req) as resp:
        return json.loads(resp.read().decode())


def fetch_all_pages(url: str, token: str) -> list:
    results = []
    page = 1
    while True:
        paged = github_get(f"{url}?per_page=100&page={page}", token)
        if not paged:
            break
        results.extend(paged)
        if len(paged) < 100:
            break
        page += 1
    return results


def generate(owner: str, repo: str, pr_number: int, token: str) -> dict:
    base = "https://api.github.com"

    pr       = github_get(f"{base}/repos/{owner}/{repo}/pulls/{pr_number}", token)
    commits  = fetch_all_pages(f"{base}/repos/{owner}/{repo}/pulls/{pr_number}/commits", token)
    files    = fetch_all_pages(f"{base}/repos/{owner}/{repo}/pulls/{pr_number}/files", token)

    return {
        "id":                  pr["id"],
        "number":              pr["number"],
        "title":               pr["title"],
        "description":         pr.get("body") or "",
        "state":               pr["state"],
        "author":              pr["user"]["login"],
        "created_at":          pr["created_at"],
        "updated_at":          pr["updated_at"],
        "merged_at":           pr.get("merged_at"),
        "owner":               owner,
        "repo":                repo,
        "url":                 pr["html_url"],
        "additions":           pr.get("additions", 0),
        "deletions":           pr.get("deletions", 0),
        "changed_files_count": pr.get("changed_files", len(files)),
        "commits": [
            {
                "sha":          c["sha"],
                "message":      c["commit"]["message"],
                "author":       c["commit"]["author"]["name"],
                "author_email": c["commit"]["author"]["email"],
                "timestamp":    c["commit"]["author"]["date"],
                "url":          c["html_url"],
            }
            for c in commits
        ],
        "changed_files": [
            {
                "filename":          f["filename"],
                "status":            f["status"],
                "additions":         f["additions"],
                "deletions":         f["deletions"],
                "changes":           f["changes"],
                "patch":             f.get("patch", ""),
                "previous_filename": f.get("previous_filename", ""),
                "blob_url":          f.get("blob_url", ""),
                "raw_url":           f.get("raw_url", ""),
            }
            for f in files
        ],
    }


if __name__ == "__main__":
    if len(sys.argv) != 4:
        print("Usage: python3 generate_pr_json.py <owner> <repo> <pr_number>")
        sys.exit(1)

    token = os.environ.get("GITHUB_TOKEN")
    if not token:
        print("Error: GITHUB_TOKEN environment variable is required")
        sys.exit(1)

    owner, repo, number = sys.argv[1], sys.argv[2], int(sys.argv[3])
    data = generate(owner, repo, number, token)

    filename = f"pr_{owner}_{repo}_{number}.json"
    with open(filename, "w") as f:
        json.dump(data, f, indent=2)

    print(f"Generated: {filename}")
    print(f"  PR:      #{data['number']} - {data['title']}")
    print(f"  Author:  {data['author']}")
    print(f"  Commits: {len(data['commits'])}")
    print(f"  Files:   {len(data['changed_files'])}")
