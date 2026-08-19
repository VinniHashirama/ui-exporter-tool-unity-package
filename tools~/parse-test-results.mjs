// Resume o XML do NUnit que o test runner da Unity gera.
// Regex em vez de parser de XML de propósito: sem dependências, e o formato é estável.
import { readFileSync } from 'node:fs'

const path = process.argv[2]
if (path === undefined) {
  console.error('uso: parse-test-results.mjs <test-results.xml>')
  process.exit(2)
}

const xml = readFileSync(path, 'utf8')

const attr = (tag, name) => tag.match(new RegExp(`${name}="([^"]*)"`))?.[1]

const run = xml.match(/<test-run\b[^>]*>/)?.[0]
if (run === undefined) {
  console.error('XML sem elemento <test-run>')
  process.exit(2)
}

const total = Number(attr(run, 'total') ?? 0)
const passed = Number(attr(run, 'passed') ?? 0)
const failed = Number(attr(run, 'failed') ?? 0)
const skipped = Number(attr(run, 'skipped') ?? 0)

for (const suite of xml.matchAll(/<test-suite\b[^>]*type="TestFixture"[^>]*>/g)) {
  const name = attr(suite[0], 'name')
  const suitePassed = attr(suite[0], 'passed') ?? '0'
  const suiteFailed = attr(suite[0], 'failed') ?? '0'
  const mark = suiteFailed === '0' ? ' ' : '!'
  console.log(`  ${mark} ${name.padEnd(34)} ${suitePassed} passou, ${suiteFailed} falhou`)
}

// Cada <test-case> que não passou vem com a mensagem e o stack no bloco seguinte.
const cases = [...xml.matchAll(/<test-case\b[^>]*>([\s\S]*?)<\/test-case>|<test-case\b[^>]*\/>/g)]

for (const match of cases) {
  const tag = match[0].match(/<test-case\b[^>]*?>/)?.[0] ?? match[0]
  if (attr(tag, 'result') === 'Passed') continue

  console.log('')
  console.log(`FALHOU  ${attr(tag, 'fullname')}`)

  const body = match[1] ?? ''
  const message = body.match(/<message>([\s\S]*?)<\/message>/)?.[1]
  if (message !== undefined) {
    console.log(
      decode(message)
        .trim()
        .split('\n')
        .slice(0, 12)
        .map((line) => `        ${line}`)
        .join('\n'),
    )
  }

  const stack = body.match(/<stack-trace>([\s\S]*?)<\/stack-trace>/)?.[1]
  const frame = decode(stack ?? '')
    .split('\n')
    .find((line) => line.includes('.cs:'))
  if (frame !== undefined) {
    console.log(`        em ${frame.trim()}`)
  }
}

console.log('')
console.log(`total ${total} | passou ${passed} | falhou ${failed} | pulou ${skipped}`)

process.exit(failed > 0 || total === 0 ? 1 : 0)

function decode(value) {
  return value
    .replace(/<!\[CDATA\[([\s\S]*?)\]\]>/g, '$1')
    .replace(/&lt;/g, '<')
    .replace(/&gt;/g, '>')
    .replace(/&quot;/g, '"')
    .replace(/&apos;/g, "'")
    .replace(/&amp;/g, '&')
}
