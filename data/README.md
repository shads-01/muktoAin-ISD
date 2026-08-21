# MuktoAin Data Directory

This directory contains static seed files, lookup tables, and information on required legal datasets.

---

## 1. Seed Files (Committed to Git)

| File | Description | Source / Usage |
|---|---|---|
| `districts.json` | 64 Bangladesh administrative districts | Seed data for `DISTRICT` table |
| `categories.json` | 4 core legal case categories (Labour, GD, RTI, Consumer) | Seed data for `CASE_CATEGORY` table |
| `scenario-mappings.json` | Hand-curated keyword mappings (Bangla, English, Banglish) | Seed data for `SCENARIO_MAPPING` table |

---

## 2. External Large Datasets (Ignored in Git)

Due to file size and licensing, large statutory corpora and benchmark files are not tracked in Git. Teammates should download them from Kaggle and place them in this `data/` directory:

### 2.1 Bangladesh Legal Acts Dataset
- **Filename:** `data/bangladesh-acts-dataset.json`
- **Kaggle Link:** [kaggle.com/datasets/sakhadib/bangladesh-legal-acts-dataset](https://www.kaggle.com/datasets/sakhadib/bangladesh-legal-acts-dataset)
- **Description:** Scraped statutory acts of Bangladesh (Laws of Bangladesh) covering historical and modern legislation.
- **Licensing:** CC BY-SA 4.0 (Attribution required in project documentation).
- **Format:** JSON array of Act objects (with Title, ActNumber, Year, Sections, Footnotes).

### 2.2 Bangladesh Legal QA Benchmark Dataset (Checkpoint 3)
- **Filename:** `data/bangladesh-legal-qa-dataset.json`
- **Kaggle Link:** [kaggle.com/datasets/momahadi/bangladesh-legal-qa-dataset](https://www.kaggle.com/datasets/momahadi/bangladesh-legal-qa-dataset)
- **Description:** 2,165 curated Bangladeshi legal QA pairs with ground-truth statutory section references used for evaluation benchmarks.

---

## 3. Verification

To verify your downloaded JSON files on Windows PowerShell:
```powershell
Get-FileHash data/bangladesh-acts-dataset.json -Algorithm SHA256
```
