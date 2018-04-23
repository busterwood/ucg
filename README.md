# Universal Code Generator

A data-driven code generator with templates and a (very) simple scripting language.  Written in .NET Core 2.0 so it should run on Mac/Linux/Windows.

The script language (see below) uses XPATH 1.0 expressions for iteration, conditions and text subsitutions.

Use can use `ucg` for [Model Oriented Programming](https://github.com/imatix/gsl#model-oriented-programming), working with higher-level abstractions than general purpose languages.  `ucg` will typically be used to generate one or more [patterns](https://en.wikipedia.org/wiki/Software_design_pattern) from a source XML model.

`ucg` is insprired by [iMatrix's GSL](https://github.com/imatix/gsl).  I tried and failed to get GSL to work on Win x64, so I thought I could write something similar for .NET Core.  `ucg` is pretty simple and comes in at less than 800 LOC.

## Running

UCG uses two types of files to generate any code you can imagine:

1. XML model to generate from.
2. Script files, which can be either _pure scripts_ or _templates_.

To generate pass the script name and model name to ucg, for example:
```
dotnet ucg --script my-script.ucg my-model.xml
```

The script is then interpreted and output is sent to `StdOut` which can be changed via the `output` keyword (see below).

Any additional arguments _after the model file name_ are added as attributes to the root model element, for example:
```
dotnet ucg --script my-script.ucg my-model.xml --cs-namespace BusterWood.Samples
```

## Models

Models are XML files that you define.  An example would be a a list of entities (tables) that you wish to generate code for:

```xml
<?xml version="1.0" encoding="utf-8" ?>
<entities cs-namespace="BusterWood.Ucg">
  <entity name="User">
    <field name="User Id" nulls="not null" type="int" pk="true"/>
    <field name="Full Name" nulls="not null" type="string" db-type="VarChar" db-size="50"/>
    <field name="Email" nulls="not null" type="string" db-type="VarChar" db-size="50"/>
  </entity>
</entities>
```

Models are freeform XML, there is no "special" tags,  but we recommend using:
* _attributes_ for properties with at most one value
* _elements_ for lists of things

## Templates

When a script is in _template_ mode then input scripts are written to the output.  All expressions in the for `$(...)` expanded (see below) before the line is output.

Any line with a first character of `.` is interpreted as script language, for example:
```
.template on

	public class $(@name:p)
	{
. foreach field
		public $(@type) $(@name:p) { get; set; }
. endfor
	}
```

## Expressions

Expressions like `$(...)` are _recursively_ expanded in the script language and as well as in templates.  
Expressions are evaluated on the _current model element_, which is initally the root element of the XML, but this is changed by `foreach` blocks (see below).

Expressions can be either:
* an XPATH expression on the current model, e.g. `$(@name)` to get the value of the name attribute.
* double quoted text, typically used with the `:b`, `:,` and `:~` modifiers (see below), for example `$(" AND":~)`

The value returned by an expression can be used as-is, for modified via one of the following format specifications:
* `:u` for `UPPER CASE`, e.g. `$(@name:u)`
* `:l` for `lower case`, e.g. `$(@name:l)`
* `:t` for `Title Case`, e.g. `$(@name:t)`
* `:p` for `PascalCase`, e.g. `$(@name:p)`
* `:c` for `camelCase`, e.g. `$(@name:c)`
* `:sql` for `SQL_CASE`, e.g. `$(@name:sql)`
* `:b` for `(surround with brackets)` empty string when empty, otherwise add brackets round the text
* `:,` for `,prefixed with comma` empty string when empty, otherwise the value with a comma added at the beginning
* `:~` means don't output the value for the last item in a `foreach` or `forfiles` loop.

The `b`, `,` and `~` modifiers can be combined with other modifiers, for example `$(@db-size:b,)`

Expressions can contain `??` which is interpreted as the left hand side, if that has value, otherwise the right hand side.  For example:
```
new SqlMetaData("$(@name:sql)", SqlDbType.$(@db-type??@type)$(@db-size:b,))$(",":~)
```

## Script language

### comments

Comment lines start with `//`, for example:
```
// this is a comment
```

### template

Turns _template_ mode on or off, for example:
```
.template on
```

### output

The `output` keyword changes the file that the script to writes to.  If the file exists then it is __overwritten__.

For example, the following sets the output file to be the value of the `name` attribute of the current model element:
```
output "$(@name).cs"
```

### foreach/endfor

A `foreach` block repeats the code inside the block for each _element_ matching the XPATH expression.  
Inside the block then the current node is changed to be the selected child node.

For example, the following code runs another script for each child element with the name `entity`:
```
foreach entity
	include "insert-proc.ucg"
endfor
```

### forfiles/endfiles

A `forfiles` block repeats the code inside the block for each file found. Each file found is added as a child element of the current model with the following attributes:
* `path` which is the full path to the file
* `name` which is the file name _without_ the file extension
* `extension` which is the file extension
* `folder` which is the name of the directory which contains the file

Inside the block then the current node has a `file` child element added.

For example, the following code runs each script found in the current directory with the extension `.ucg`:
```
forfiles "*.ucg"
	include "$(file/@path)"
endfiles
```

### if/else/endif

A `if` block conditional runs the code in the block if the XPATH expression:
* evaluates to a non-empty string
* evalutes to a single element

When an `else` block is present then that code is run only when the the condition is _false_.

For example, the following code runs another script if the `fk-rel` attribute exists and is not empty
```
if string(@fk-rel)
	include "select-fk.ucg"
endif
```

### loadxml

The `loadxml` statement adds child elements to current model from an XML file where the elements matche an XPATH expression.

For example, the following loads `entity` elements where the name attribute has a value of `order` from the `schema.xml` file:
```
loadxml "schema.xml" entity[name='order']
foreach entity
	...
endfor
```

### insertxml

The `insertxml` keyword writes some elements of the model XML to the output.
* if no paramter is supplied then the current model element is written.
* if an xpath expression is suppplied then elements matching that expression are written to the output.

For example:
```
/* 
WARNING: This file is generated for the following model:

.insertxml

*/
```

### echo

The `echo` keyword writes some text to StdErr.  Any expression in the text are expanded before writing to StdErr.

For example:
```
.echo hello world!
```
## Notes

XPATH 1.0 expressions are supported with the addition on the `distinct-values()` function.  
The `distinct-values()` function works a bit differently in that it __only__ compares the _first_ attribute name and value and the _text_ of the current element.

### XPath examples

* `@name` returns the value of the `name` attribute.
* `../@name` returns the value of the `name` attribute _of the parent element_.
* `state[@terminal]` returns child `state` elements that have a `terminal` attribute.
* `state[not(@terminal)]` returns child `state` elements that _do not_ have a `terminal` attribute.
* `distinct-values(.//do)` returns a set of _decendent_ `do` elements that have unique _text_ and _first_ attribute.
