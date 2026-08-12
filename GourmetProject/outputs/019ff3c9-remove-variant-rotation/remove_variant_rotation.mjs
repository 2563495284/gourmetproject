import fs from "node:fs/promises";
import path from "node:path";
import { FileBlob, SpreadsheetFile } from "@oai/artifact-tool";

const root = "/Users/hcm-b0451/gourmetproject/GourmetProject";
const outputDir = path.join(root, "outputs/019ff3c9-remove-variant-rotation");

async function shiftColumnLeft({ sourcePath, outputName, sheetName, deleteColumn, lastColumn, lastRow, expectedHeader }) {
  const workbook = await SpreadsheetFile.importXlsx(await FileBlob.load(sourcePath));
  const sourceWorkbook = await SpreadsheetFile.importXlsx(await FileBlob.load(sourcePath));
  const sheet = workbook.worksheets.getItem(sheetName);
  const sourceSheet = sourceWorkbook.worksheets.getItem(sheetName);
  if (sheet.getRange(`${deleteColumn}1`).values?.[0]?.[0] !== expectedHeader) {
    throw new Error(`${sheetName}!${deleteColumn}1 is not ${expectedHeader}; refusing to shift columns`);
  }

  const colToNumber = (name) => {
    let value = 0;
    for (const char of name) value = value * 26 + char.charCodeAt(0) - 64;
    return value;
  };
  const numberToCol = (value) => {
    let name = "";
    while (value > 0) {
      value -= 1;
      name = String.fromCharCode(65 + (value % 26)) + name;
      value = Math.floor(value / 26);
    }
    return name;
  };

  const deleteIndex = colToNumber(deleteColumn);
  const lastIndex = colToNumber(lastColumn);
  const sourceStart = numberToCol(deleteIndex + 1);
  const destinationLast = numberToCol(lastIndex - 1);
  const sourceTail = sourceSheet.getRange(`${sourceStart}1:${lastColumn}${lastRow}`);
  const typedValues = sourceTail.values;

  sheet.getRange(`${deleteColumn}1:${lastColumn}${lastRow}`).clear({ applyTo: "all" });
  const destinationTail = sheet.getRange(`${deleteColumn}1:${destinationLast}${lastRow}`);
  destinationTail.copyFrom(sourceTail, "all");
  const translatedFormulas = destinationTail.formulas;
  destinationTail.values = typedValues;
  for (let row = 0; row < translatedFormulas.length; row += 1) {
    for (let col = 0; col < translatedFormulas[row].length; col += 1) {
      const formula = translatedFormulas[row][col];
      if (formula) sheet.getRange(`${numberToCol(deleteIndex + col)}${row + 1}`).formulas = [[formula]];
    }
  }

  const outputPath = path.join(outputDir, outputName);
  const output = await SpreadsheetFile.exportXlsx(workbook);
  await output.save(outputPath);
  return outputPath;
}

await fs.mkdir(outputDir, { recursive: true });

const dishPath = await shiftColumnLeft({
  sourcePath: path.join(root, "GameConfig/Datas/dish.xlsx"),
  outputName: "dish.xlsx",
  sheetName: "dish_variant",
  deleteColumn: "K",
  lastColumn: "L",
  lastRow: 292,
  expectedHeader: "rotation",
});

const enumSourcePath = path.join(root, "GameConfig/Datas/__enums_dish.xlsx");
const enumWorkbook = await SpreadsheetFile.importXlsx(await FileBlob.load(enumSourcePath));
const enumSheet = enumWorkbook.worksheets.getItem("Sheet1");
if (enumSheet.getRange("B4").values?.[0]?.[0] !== "DishRotation") {
  throw new Error("Sheet1!B4 is not DishRotation; refusing to clear enum rows");
}
enumSheet.getRange("A4:L7").clear({ applyTo: "all" });
const enumPath = path.join(outputDir, "__enums_dish.xlsx");
const enumOutput = await SpreadsheetFile.exportXlsx(enumWorkbook);
await enumOutput.save(enumPath);

for (const { label, filePath, sheetName, range } of [
  { label: "dish_variant", filePath: dishPath, sheetName: "dish_variant", range: "A1:K63" },
  { label: "enums_dish", filePath: enumPath, sheetName: "Sheet1", range: "A1:L7" },
]) {
  const workbook = await SpreadsheetFile.importXlsx(await FileBlob.load(filePath));
  const inspection = await workbook.inspect({
    kind: "table",
    range: `${sheetName}!${range}`,
    include: "values,formulas",
    tableMaxRows: 8,
    tableMaxCols: 14,
    maxChars: 12000,
  });
  console.log(inspection.ndjson);
  const stale = await workbook.inspect({
    kind: "match",
    searchTerm: "rotation|DishRotation|Deg0|Deg90|Deg180|Deg270",
    options: { useRegex: true, maxResults: 100 },
    maxChars: 6000,
  });
  console.log(stale.ndjson);
  const errors = await workbook.inspect({
    kind: "match",
    searchTerm: "#REF!|#DIV/0!|#VALUE!|#NAME\\?|#N/A",
    options: { useRegex: true, maxResults: 300 },
    maxChars: 12000,
  });
  console.log(errors.ndjson);
  const preview = await workbook.render({ sheetName, range, scale: 2, format: "png" });
  await fs.writeFile(
    path.join(outputDir, `after_${label}.png`),
    new Uint8Array(await preview.arrayBuffer()),
  );
}

console.log(JSON.stringify({ dishPath, enumPath }));
