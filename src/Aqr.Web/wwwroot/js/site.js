// Explorer: apply filters as soon as a chip or select changes (the form is a plain GET, so it also works without JS).
(() => {
  const form = document.getElementById('filters');
  if (!form) return;
  form.addEventListener('change', e => {
    if (e.target.matches('input[type=checkbox], select, input[type=range]')) form.requestSubmit();
  });
})();
