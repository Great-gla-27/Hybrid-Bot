# Security Notice

This repository contains research and educational code for an automated trading system.

**Important:**
- This project is not financial advice.
- It is intended for learning, backtesting, and demonstration purposes only.
- Do not use this code with live accounts or real money without extensive independent testing.

**Good Practices:**
- Never commit or share API keys, broker logins, or account numbers in code.
- cTrader ID passwords are read from a plain text file mounted into the Docker container at runtime (see README) - never bake credentials into the image or the Dockerfile.
- Only use open, publicly available datasets. Proprietary or paid datasets should not be uploaded here.

**Disclaimer:**
The author assumes no responsibility for financial losses, errors, or damages resulting from the use of this software. Use entirely at your own risk.
