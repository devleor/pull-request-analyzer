# Security Policy

## Supported Versions

Use this section to tell people about which versions of your project are
currently being supported with security updates.

| Version | Supported          |
| ------- | ------------------ |
| 1.0.x   | :white_check_mark: |
| < 1.0   | :x:                |

## Reporting a Vulnerability

We take all security bugs in Pull Request Analyzer seriously. Thank you for improving the security of our project. We appreciate your efforts and responsible disclosure and will make every effort to acknowledge your contributions.

To report a security vulnerability, please use the [GitHub Security Advisory "Report a Vulnerability"](https://github.com/devleor/pull-request-analyzer/security/advisories/new) feature.

Alternatively, you can email the project maintainers at [INSERT EMAIL ADDRESS].

**Please do not report security vulnerabilities through public GitHub issues.**

### What to Include

Please include the following information in your report:

- A clear and concise description of the vulnerability.
- Steps to reproduce the vulnerability.
- The version of the project affected.
- Any potential impact of the vulnerability.
- Any suggested mitigations.

### Our Commitment

- We will acknowledge your report within 48 hours.
- We will provide a more detailed response within 72 hours, including our assessment of the vulnerability and a timeline for a fix.
- We will keep you updated on our progress.
- We will credit you for your discovery, unless you prefer to remain anonymous.

## Security Best Practices

- **Never commit secrets** to the repository. Use environment variables or a secret management tool.
- **Keep dependencies up to date** to avoid known vulnerabilities.
- **Run security scans** as part of your CI/CD pipeline.

## Disclosure Policy

When the security team receives a security bug report, they will assign it to a primary handler. This person will coordinate the fix and release process, involving the following steps:

1.  **Confirm the problem** and determine the affected versions.
2.  **Audit code** to find any similar problems.
3.  **Prepare a fix** for all supported versions. This fix will be prepared in a private fork of the repository.
4.  **Release the fix** and publish a security advisory.

We will do our best to handle the bug in a timely manner and will notify you when the fix is released.
