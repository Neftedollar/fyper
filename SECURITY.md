# Security Policy

## Supported Versions

| Version | Supported |
|---------|-----------|
| 1.x     | Yes       |
| < 1.0   | No        |

## Reporting a Vulnerability

If you discover a security vulnerability in Fyper, please report it responsibly:

1. **Do not** open a public GitHub issue
2. Email: [create a private security advisory](https://github.com/Neftedollar/fyper/security/advisories/new) on GitHub
3. Include steps to reproduce and impact assessment

We will respond within 48 hours and aim to release a fix within 7 days for critical issues.

## Security Design

Fyper's primary security feature is **mandatory parameterization**: all literal values become `$p0`, `$p1` parameters, never inlined into Cypher strings. This prevents Cypher injection by design.

The parameterization invariant is verified by FsCheck property-based tests.
