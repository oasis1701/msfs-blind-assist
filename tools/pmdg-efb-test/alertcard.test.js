const { test } = require('node:test');
const assert = require('node:assert');
const { scrape } = require('./run');

// EFB alerts (#alert_card: heading + message + OK) popped up on Save Preferences / errors but were
// never auto-announced. The agent now emits ONE assertive 'alert' item the host form speaks on
// appearance, skips the card's heading/message in the main pass (no duplicate), keeps the OK button.
test('EFB alert card -> one assertive alert item; OK kept; no duplicate text', () => {
  const els = scrape('alertcard');
  const alerts = els.filter(e => e.kind === 'alert');
  assert.equal(alerts.length, 1, 'exactly one alert item');
  assert.equal(alerts[0].live, 'assertive', 'flagged assertive so it interrupts');
  assert.equal(
    alerts[0].text,
    'Success: Tablet preferences were updated. Hoppie ID is invalid and was cleared.',
    'heading + message, with a space re-inserted after the sentence period');
  assert.ok(els.some(e => e.kind === 'button' && e.text === 'OK'), 'OK button still present + clickable');
  assert.ok(!els.some(e => e.kind !== 'alert' && /Tablet preferences/.test(e.text || '')),
    'no duplicate message line outside the alert item');
});

// The sentence-boundary space re-insertion must fire only before a CAPITAL letter (a new sentence),
// never inside a number — "Fuel set to 12.5 tonnes.Confirm ..." must keep "12.5" intact while still
// spacing "tonnes. Confirm".
test('EFB alert card -> decimal in message is preserved, sentence boundary still spaced', () => {
  const alerts = scrape('alertcard_number').filter(e => e.kind === 'alert');
  assert.equal(alerts.length, 1, 'exactly one alert item');
  assert.equal(
    alerts[0].text,
    'Notice: Fuel set to 12.5 tonnes. Confirm before departure.',
    'decimal kept, sentence boundary spaced');
});
