# Dataset Attribution & Open Data Licenses

This document details the external datasets utilized in the **MuktoAin (মুক্ত আইন)** legal-aid platform, including their sources, licenses, intended use, and modifications in compliance with the **Creative Commons Attribution-ShareAlike 4.0 International (CC BY-SA 4.0)** and related open-access licenses.

---

## 1. Bangladesh Legal Acts Dataset

- **Source**: [`sakhadib/bangladesh-legal-acts-dataset`](https://www.kaggle.com/datasets/sakhadib/bangladesh-legal-acts-dataset) (Kaggle)
- **Primary Source Law Corpus**: Laws of Bangladesh (Ministry of Law, Justice and Parliamentary Affairs)
- **License**: [Creative Commons Attribution-ShareAlike 4.0 International (CC BY-SA 4.0)](https://creativecommons.org/licenses/by-sa/4.0/)
- **Usage**: Batch-imported into MuktoAin's statutory database for Retrieval-Augmented Generation (RAG) and citizen legal research.
- **Corpus Summary**: 1,484 Acts of Bangladesh, comprising 35,633 legal sections and 14,523 footnotes.
- **Modifications & Derived Works**:
  - Parsed and normalized into a structured relational hierarchy: `ACT` $\rightarrow$ `ACT_SECTION` $\rightarrow$ `ACT_SECTION_CHUNK` $\rightarrow$ `ACT_FOOTNOTE`.
  - Chunked into 42,858 sub-section semantic chunks with sliding context windows for vector embedding generation (`gemini-embedding-001`).
  - Indexed in Microsoft SQL Server Full-Text Search (FTS) catalog for keyword search and fallback retrieval.
  - Mapped to predefined common legal scenarios (`SCENARIO_MAPPING`) for rapid retrieval boosting.

---

## 2. Bangladesh Legal QA Dataset

- **Source**: [`momahadi/bangladesh-legal-qa-dataset`](https://huggingface.co/datasets/momahadi/bangladesh-legal-qa-dataset) (Hugging Face)
- **License**: [Creative Commons Attribution 4.0 International (CC BY 4.0)](https://creativecommons.org/licenses/by/4.0/)
- **Usage**: Evaluation benchmark and validation suite for testing legal inquiry answering and retrieval precision.
- **Corpus Summary**: 2,165 annotated legal question-and-answer pairs covering criminal, labour, family, commercial, and constitutional legal matters in Bangladesh.
- **Modifications & Derived Works**:
  - Filtered and structured into automated test harness inputs for zero-shot and few-shot IRAC prompt evaluation.
  - Linked to relevant statutory sections within the MuktoAin corpus for grounded citation verification.

---

## 3. Font & Static Assets Attribution

- **Font**: [Noto Sans Bengali](https://fonts.google.com/noto/specimen/Noto+Sans+Bengali) (Google Fonts)
  - **License**: [SIL Open Font License 1.1 (OFL)](https://openfontlicense.org/)
  - **Usage**: Server-side PDF rendering via QuestPDF and client-side web font display.
- **Icons**: [Lucide Icons](https://lucide.dev/)
  - **License**: [ISC License](https://opensource.org/licenses/ISC)
  - **Usage**: UI action icons and navigation chrome.

---

## 4. License Summary & Compliance Statement

Under the terms of CC BY-SA 4.0:
- **Attribution**: Proper credit to original dataset creators is provided above.
- **ShareAlike**: Any public adaptations or redistributions of the derived legal corpus must be shared under equivalent open licensing terms.
- **Disclaimer**: The legal statutes and QA datasets provided in this project are for informational, academic, and augmented legal-aid assistance purposes only and do not constitute formal legal advice.
