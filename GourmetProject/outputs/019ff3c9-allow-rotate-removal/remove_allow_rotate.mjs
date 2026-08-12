import fs from "node:fs/promises";
import path from "node:path";
import { FileBlob, SpreadsheetFile } from "@oai/artifact-tool";

const root = "/Users/hcm-b0451/gourmetproject/GourmetProject";
const sourcePath = path.join(root, "GameConfig/Datas/dish.xlsx");
const outputDir = path.join(root, "outputs/019ff3c9-allow-rotate-removal");
const outputPath = path.join(outputDir, "dish.xlsx");

const workbook = await SpreadsheetFile.importXlsx(await FileBlob.load(sourcePath));
const sourceWorkbook = await SpreadsheetFile.importXlsx(await FileBlob.load(sourcePath));
const sheet = workbook.worksheets.getItem("dish_base");
const sourceSheet = sourceWorkbook.worksheets.getItem("dish_base");
const used = sheet.getUsedRange();
if (!used || used.address !== "A1:Z122") {
  throw new Error(`Unexpected dish_base used range: ${used?.address ?? "none"}`);
}
if (sheet.getRange("E1").values?.[0]?.[0] !== "allowRotate") {
  throw new Error("dish_base!E1 is not allowRotate; refusing to shift columns");
}

// Remove the schema/data column while preserving all existing workbook content:
// shift the complete populated tail left once, then clear the old final column.
// copyFrom preserves formatting, but imported numeric cells can be coerced to strings,
// so restore the typed source values and translated formulas afterward.
const sourceTail = sourceSheet.getRange("F1:Z122");
const typedValues = sourceTail.values;
sheet.getRange("E1:Z122").clear({ applyTo: "all" });
const destinationTail = sheet.getRange("E1:Y122");
destinationTail.copyFrom(sourceTail, "all");
const translatedFormulas = destinationTail.formulas;
destinationTail.values = typedValues;
const columnName = (oneBasedColumn) => {
  let value = oneBasedColumn;
  let name = "";
  while (value > 0) {
    value -= 1;
    name = String.fromCharCode(65 + (value % 26)) + name;
    value = Math.floor(value / 26);
  }
  return name;
};
for (let row = 0; row < translatedFormulas.length; row += 1) {
  for (let col = 0; col < translatedFormulas[row].length; col += 1) {
    const formula = translatedFormulas[row][col];
    if (formula) {
      sheet.getRange(`${columnName(col + 5)}${row + 1}`).formulas = [[formula]];
    }
  }
}

await fs.mkdir(outputDir, { recursive: true });
const output = await SpreadsheetFile.exportXlsx(workbook);
await output.save(outputPath);

// Re-import the saved artifact for verification so rendering/recalculation cannot
// mutate the workbook that is ultimately delivered.
const verificationWorkbook = await SpreadsheetFile.importXlsx(await FileBlob.load(outputPath));
const verificationSheet = verificationWorkbook.worksheets.getItem("dish_base");
const header = await verificationWorkbook.inspect({
  kind: "table",
  range: "dish_base!A1:K8",
  include: "values,formulas",
  tableMaxRows: 8,
  tableMaxCols: 11,
  maxChars: 8000,
});
console.log(header.ndjson);

const stale = await verificationWorkbook.inspect({
  kind: "match",
  searchTerm: "allowRotate",
  options: { useRegex: false, maxResults: 50 },
  maxChars: 4000,
});
console.log(stale.ndjson);

const errors = await verificationWorkbook.inspect({
  kind: "match",
  searchTerm: "#REF!|#DIV/0!|#VALUE!|#NAME\\?|#N/A",
  options: { useRegex: true, maxResults: 300 },
  maxChars: 12000,
});
console.log(errors.ndjson);

const preview = await verificationWorkbook.render({
  sheetName: "dish_base",
  autoCrop: "all",
  scale: 1,
  format: "png",
});
await fs.writeFile(
  path.join(outputDir, "after_dish_base.png"),
  new Uint8Array(await preview.arrayBuffer()),
);

console.log(JSON.stringify({ outputPath, usedRange: verificationSheet.getUsedRange()?.address ?? null }));
