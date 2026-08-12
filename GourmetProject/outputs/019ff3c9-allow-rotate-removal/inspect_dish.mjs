import fs from "node:fs/promises";
import path from "node:path";
import { FileBlob, SpreadsheetFile } from "@oai/artifact-tool";

const root = "/Users/hcm-b0451/gourmetproject/GourmetProject";
const outputDir = path.join(root, "outputs/019ff3c9-allow-rotate-removal/before");
const input = await FileBlob.load(path.join(root, "GameConfig/Datas/dish.xlsx"));
const workbook = await SpreadsheetFile.importXlsx(input);

await fs.mkdir(outputDir, { recursive: true });

const summary = await workbook.inspect({
  kind: "workbook,sheet,table",
  maxChars: 12000,
  tableMaxRows: 8,
  tableMaxCols: 24,
  tableMaxCellChars: 120,
});
console.log(summary.ndjson);

const sheets = workbook.worksheets.items;
for (let index = 0; index < sheets.length; index += 1) {
  const sheet = sheets[index];
  const used = sheet.getUsedRange();
  const usedAddress = used?.address ?? null;
  console.log(JSON.stringify({ index, name: sheet.name, usedAddress }));

  const preview = await workbook.render({
    sheetName: sheet.name,
    autoCrop: "all",
    scale: 1,
    format: "png",
  });
  const safeName = sheet.name.replaceAll(/[^\p{L}\p{N}_.-]+/gu, "_");
  await fs.writeFile(
    path.join(outputDir, `${String(index + 1).padStart(2, "0")}_${safeName}.png`),
    new Uint8Array(await preview.arrayBuffer()),
  );
}

const matches = await workbook.inspect({
  kind: "match",
  searchTerm: "allowRotate",
  options: { useRegex: false, maxResults: 50 },
  maxChars: 6000,
});
console.log(matches.ndjson);
