# Contributing to Pull Request Analyzer

First off, thank you for considering contributing to Pull Request Analyzer! It's people like you that make this project great.

## Code of Conduct

This project and everyone participating in it is governed by our [Code of Conduct](CODE_OF_CONDUCT.md). By participating, you are expected to uphold this code. Please report unacceptable behavior to the project maintainers.

## How Can I Contribute?

### Reporting Bugs

- **Ensure the bug was not already reported** by searching on GitHub under [Issues](https://github.com/devleor/pull-request-analyzer/issues).
- If you're unable to find an open issue addressing the problem, [open a new one](https://github.com/devleor/pull-request-analyzer/issues/new). Be sure to include a **title and clear description**, as much relevant information as possible, and a **code sample** or an **executable test case** demonstrating the expected behavior that is not occurring.

### Suggesting Enhancements

- Open a new issue with the title `[Enhancement]` and describe the new feature or enhancement you would like to see.
- Provide as much detail and context as possible.

### Pull Requests

1. Fork the repository and create your branch from `main`.
2. If you've added code that should be tested, add tests.
3. If you've changed APIs, update the documentation.
4. Ensure the test suite passes (`make test`).
5. Make sure your code lints (`make lint`).
6. Issue that pull request!

## Styleguides

### Git Commit Messages

- Use the present tense ("Add feature" not "Added feature").
- Use the imperative mood ("Move cursor to..." not "Moves cursor to...").
- Limit the first line to 72 characters or less.
- Reference issues and pull requests liberally after the first line.

### C# Styleguide

- Follow the [.NET Runtime coding style](https://github.com/dotnet/runtime/blob/main/docs/coding-guidelines/coding-style.md).
- Use `dotnet format` to automatically format your code (`make format`).

## Development Setup

1. **Clone the repository**:
   ```bash
   git clone https://github.com/devleor/pull-request-analyzer.git
   cd pull-request-analyzer
   ```

2. **Setup the environment**:
   ```bash
   make setup
   ```

3. **Run the development server**:
   ```bash
   make dev
   ```

## Questions?

If you have any questions, feel free to open an issue and we will do our best to help you out.
