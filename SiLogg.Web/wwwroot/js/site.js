const collator = new Intl.Collator("sv-SE", {
  numeric: true,
  sensitivity: "base",
});

document.querySelectorAll("table[data-sortable]").forEach((table) => {
  const headers = table.querySelectorAll("thead th");

  headers.forEach((header, columnIndex) => {
    if (header.hasAttribute("data-no-sort")) {
      return;
    }

    const label = header.textContent.trim();
    const button = document.createElement("button");
    button.type = "button";
    button.className = "sort-button";
    button.innerHTML = `<span>${label}</span><span class="sort-indicator" aria-hidden="true">↕</span>`;
    button.setAttribute("aria-label", `Sortera efter ${label}`);
    header.textContent = "";
    header.append(button);
    header.setAttribute("aria-sort", "none");

    button.addEventListener("click", () => {
      const ascending = header.getAttribute("aria-sort") !== "ascending";
      headers.forEach((otherHeader) =>
        otherHeader.setAttribute("aria-sort", "none"),
      );
      header.setAttribute("aria-sort", ascending ? "ascending" : "descending");

      const rows = Array.from(table.tBodies[0]?.rows ?? []);
      rows.sort((leftRow, rightRow) => {
        const leftValue = getSortValue(leftRow.cells[columnIndex]);
        const rightValue = getSortValue(rightRow.cells[columnIndex]);
        const comparison = compareValues(leftValue, rightValue);
        return ascending ? comparison : -comparison;
      });

      rows.forEach((row) => table.tBodies[0].append(row));
    });
  });
});

function getSortValue(cell) {
  return (cell?.dataset.sortValue ?? cell?.textContent ?? "").trim();
}

function compareValues(left, right) {
  const leftEmpty = left === "" || left === "–" || left === "Saknas";
  const rightEmpty = right === "" || right === "–" || right === "Saknas";
  if (leftEmpty !== rightEmpty) return leftEmpty ? 1 : -1;

  const leftNumber = Number(left);
  const rightNumber = Number(right);
  if (Number.isFinite(leftNumber) && Number.isFinite(rightNumber)) {
    return leftNumber - rightNumber;
  }

  return collator.compare(left, right);
}
