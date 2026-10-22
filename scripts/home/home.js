// m1lator home: search filter + app counter on top of BlueStacks' js/index.js (which renders #installedApps).
$(function () {
  function refresh() {
    var q = ($("#searchBox").val() || "").trim().toLowerCase();
    var items = $("#installedApps li, .bsApps li");
    var visible = 0;
    items.each(function () {
      var name = $(this).find(".gameName").text().toLowerCase();
      var show = !q || name.indexOf(q) !== -1;
      $(this).toggle(show);
      if (show) visible++;
    });
    $("#noResults").toggle(visible === 0);
    $("#appCount").text($("#installedApps li").length);
  }

  $("#searchBox").on("input", refresh);
  $(document).on("keydown", function (e) {
    if (e.key === "Escape") { $("#searchBox").val(""); refresh(); hideSystemApps(); }
  });

  // index.js rebuilds the list on install/uninstall/download events: keep filter + count in sync
  var list = document.getElementById("installedApps");
  new MutationObserver(refresh).observe(list, { childList: true });
  refresh();
});
