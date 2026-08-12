import { FileBlob, SpreadsheetFile } from "@oai/artifact-tool";

const root = "/Users/hcm-b0451/gourmetproject/GourmetProject";
const before = await SpreadsheetFile.importXlsx(
  await FileBlob.load(`${root}/GameConfig/Datas/dish.xlsx`),
);
const after = await SpreadsheetFile.importXlsx(
  await FileBlob.load(`${root}/outputs/019ff3c9-allow-rotate-removal/dish.xlsx`),
);

const diffs = [];
const beforeSheets = before.worksheets.items;
const afterSheets = after.worksheets.items;
if (beforeSheets.length !== afterSheets.length) {
  diffs.push(`sheet count ${beforeSheets.length} != ${afterSheets.length}`);
}

for (let sheetIndex = 0; sheetIndex < beforeSheets.length; sheetIndex += 1) {
  const leftSheet = beforeSheets[sheetIndex];
  const rightSheet = afterSheets[sheetIndex];
  if (leftSheet.name !== rightSheet.name) {
    diffs.push(`sheet ${sheetIndex} name ${leftSheet.name} != ${rightSheet.name}`);
    continue;
  }
  const leftRange = leftSheet.getUsedRange();
  const rightRange = rightSheet.getUsedRange();
  if (leftSheet.name !== "dish_base") {
    const leftValues = leftRange?.values ?? [];
    const rightValues = rightRange?.values ?? [];
    const leftFormulas = leftRange?.formulas ?? [];
    const rightFormulas = rightRange?.formulas ?? [];
    if (JSON.stringify(leftValues) !== JSON.stringify(rightValues)) {
      diffs.push(`${leftSheet.name}: non-target values changed`);
    }
    if (JSON.stringify(leftFormulas) !== JSON.stringify(rightFormulas)) {
      diffs.push(`${leftSheet.name}: non-target formulas changed`);
    }
    continue;
  }

  const leftValues = leftSheet.getRange("A1:Z122").values;
  const rightValues = rightSheet.getRange("A1:Y122").values;
  for (let row = 0; row < leftValues.length; row += 1) {
    const expectedValues = leftValues[row].slice(0, 4).concat(leftValues[row].slice(5));
    if (JSON.stringify(expectedValues) !== JSON.stringify(rightValues[row])) {
      diffs.push(`dish_base row ${row + 1}: values differ outside removed column`);
    }
  }

  // Formula cells after the deleted column should shift left and Excel-adjust
  // relative references. Assert the known source formulas at their new addresses.
  for (const address of ["N1", "N2", "N3"]) {
    if (rightSheet.getRange(address).formulas?.[0]?.[0] !== '=\"\"') {
      diffs.push(`dish_base ${address}: shifted header formula missing`);
    }
  }
  if (rightSheet.getRange("N8").formulas?.[0]?.[0] !== "=ROUNDUP(M8*1.5+20,-1)") {
    diffs.push("dish_base N8: shifted formula/reference is incorrect");
  }
}

console.log(JSON.stringify({ ok: diffs.length === 0, diffs: diffs.slice(0, 50) }, null, 2));
if (diffs.length > 0) {
  process.exitCode = 1;
}
